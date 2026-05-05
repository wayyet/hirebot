using System.Text.Json;
using HireBot.Abstraction.Services.EmployeeRuntime;
using Jusoft.DingtalkStream.Core;
using Jusoft.DingtalkStream.Robot;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class DingTalkStreamBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DingTalkStreamBackgroundService> _logger;
    private readonly Dictionary<string, DingtalkStreamClient> _clients = new();

    public DingTalkStreamBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<DingTalkStreamBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("钉钉 Stream 后台服务已启动，等待实例配置...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshConnectionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新钉钉 Stream 连接时发生错误");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        await StopAllClientsAsync();
        _logger.LogInformation("钉钉 Stream 后台服务已停止");
    }

    private async Task RefreshConnectionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Repository.HireBotDbContext>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<Abstraction.Services.Security.ISecretProtector>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        var configs = await dbContext.ImConfigs
            .AsNoTracking()
            .Where(c => c.Platform == "dingtalk" && c.Status == "active")
            .ToListAsync(cancellationToken);

        var activeInstanceIds = new HashSet<string>(configs.Select(c => c.InstanceId));

        var toRemove = _clients.Keys.Where(id => !activeInstanceIds.Contains(id)).ToList();
        foreach (var instanceId in toRemove)
        {
            if (_clients.TryGetValue(instanceId, out var client))
            {
                try
                {
                    client.Dispose();
                    _logger.LogInformation("已停止实例 {InstanceId} 的钉钉 Stream 连接", instanceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "停止实例 {InstanceId} 的钉钉 Stream 连接时出错", instanceId);
                }
                _clients.Remove(instanceId);
            }
        }

        foreach (var config in configs)
        {
            if (_clients.ContainsKey(config.InstanceId))
            {
                continue;
            }

            var appKey = secretProtector.Unprotect(config.AppId);
            var appSecret = secretProtector.Unprotect(config.AppSecret);

            if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(appSecret))
            {
                _logger.LogWarning("实例 {InstanceId} 的钉钉配置缺少 AppKey 或 AppSecret，跳过 Stream 连接", config.InstanceId);
                continue;
            }

            try
            {
                var options = new DingtalkStreamOptions
                {
                    ClientId = appKey,
                    ClientSecret = appSecret,
                    AutoReplySystemMessage = true
                };
                options.Subscriptions.Add(new Subscription
                {
                    Type = "CALLBACK",
                    Topic = "/v1.0/im/bot/messages/get"
                });

                var clientLogger = loggerFactory.CreateLogger<DingtalkStreamClient>();
                var client = new DingtalkStreamClient(Options.Create(options), clientLogger);

                var handler = new DingTalkStreamMessageHandler(
                    config.InstanceId,
                    config,
                    _serviceProvider,
                    _logger);

                client.OnMessage += (_, e) => _ = handler.HandleMessageAsync(e);

                await client.Start();
                _clients[config.InstanceId] = client;
                _logger.LogInformation("实例 {InstanceId} 的钉钉 Stream 连接已建立", config.InstanceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "建立实例 {InstanceId} 的钉钉 Stream 连接失败", config.InstanceId);
            }
        }
    }

    private Task StopAllClientsAsync()
    {
        foreach (var (instanceId, client) in _clients)
        {
            try
            {
                client.Dispose();
                _logger.LogInformation("已停止实例 {InstanceId} 的钉钉 Stream 连接", instanceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "停止实例 {InstanceId} 的钉钉 Stream 连接时出错", instanceId);
            }
        }
        _clients.Clear();
        return Task.CompletedTask;
    }

    private sealed class DingTalkStreamMessageHandler
    {
        private readonly string _instanceId;
        private readonly string _ownerUserId;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public DingTalkStreamMessageHandler(
            string instanceId,
            Repository.Entities.ImConfigEntity config,
            IServiceProvider serviceProvider,
            ILogger logger)
        {
            _instanceId = instanceId;
            _ownerUserId = config.OwnerUserId;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task HandleMessageAsync(MessageEventHanderArgs e)
        {
            using var scope = _serviceProvider.CreateScope();
            var conversationService = scope.ServiceProvider.GetRequiredService<IInstanceRuntimeConversationService>();
            var replayContext = scope.ServiceProvider.GetRequiredService<IImWebhookReplayContext>();

            try
            {
                if (!e.Headers.IsRobotTopic())
                {
                    _logger.LogDebug("非机器人消息，忽略。Topic: {Topic}", e.Headers.Topic);
                    return;
                }

                var robotMessage = e.GetRobotMessageData();

                var senderStaffId = robotMessage.SenderStaffId;
                var conversationType = robotMessage.ConversationType;
                var msgId = robotMessage.MsgId;

                if (string.IsNullOrWhiteSpace(senderStaffId))
                {
                    _logger.LogWarning("钉钉 Stream 消息缺少 senderStaffId");
                    return;
                }

                if (!IsPrivateChat(conversationType))
                {
                    _logger.LogDebug("钉钉 Stream 消息为群聊，忽略。ConversationType: {ConversationType}", conversationType);
                    return;
                }

                if (robotMessage.MsgType != "text")
                {
                    _logger.LogDebug("钉钉 Stream 消息类型非文本，忽略。MsgType: {MsgType}", robotMessage.MsgType);
                    return;
                }

                var textContent = robotMessage.GetTextContent();
                var content = textContent.Content;

                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogInformation("钉钉 Stream 消息内容为空，忽略");
                    return;
                }

                if (string.IsNullOrWhiteSpace(robotMessage.SessionWebhook))
                {
                    _logger.LogWarning("钉钉 Stream 消息缺少 SessionWebhook，无法回复");
                    return;
                }

                var truncatedContent = content; //content.Length > 4000 ? content[..4000] + "\n\n[消息过长，已截断]" : content;

                var conversation = await conversationService.SendMessageAsync(
                    _instanceId,
                    "dingtalk",
                    truncatedContent,
                    _ownerUserId,
                    msgId ?? $"dt_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    senderStaffId,
                    default);

                if (!conversation.Success || conversation.Data?.AssistantMessage is null)
                {
                    _logger.LogWarning("钉钉 Stream 消息处理失败: {Message}", conversation.Message);
                    return;
                }

                await SendReplyAsync(robotMessage.SessionWebhook, conversation.Data.AssistantMessage.Content, replayContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理钉钉 Stream 消息时发生错误");
            }
        }

        private async Task SendReplyAsync(
            string sessionWebhook,
            string content,
            IImWebhookReplayContext replayContext)
        {
            if (replayContext.SkipOutboundSend)
            {
                _logger.LogInformation("钉钉 Stream 回复被跳过");
                return;
            }

            try
            {
                var cleanedContent = RemoveThinkTags(content);
                _logger.LogDebug("使用 SDK 发送钉钉机器人消息, SessionWebhook={Webhook}, content长度={Length}",
                    sessionWebhook[..Math.Min(50, sessionWebhook.Length)] + "...", cleanedContent.Length);

                await DingtalkRobotWebhookUtilites.SendTextMessage(sessionWebhook, cleanedContent);

                _logger.LogInformation("钉钉机器人消息发送成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送钉钉机器人消息时发生错误");
            }
        }

        private static string RemoveThinkTags(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            return System.Text.RegularExpressions.Regex.Replace(content, @"\<think\>.*?\</think\>", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
        }

        private static bool IsPrivateChat(string? chatType)
        {
            if (string.IsNullOrWhiteSpace(chatType))
            {
                return true;
            }

            return chatType.Trim() == "1";
        }
    }
}
