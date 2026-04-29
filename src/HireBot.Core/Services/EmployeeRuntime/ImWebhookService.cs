using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Security;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class ImWebhookService(
    HireBotDbContext dbContext,
    ISecretProtector secretProtector,
    IInstanceRuntimeConversationService runtimeConversationService,
    ILogger<ImWebhookService> logger) : IImWebhookService
{
    public async Task<ApiResponse<ImWebhookHandleResultDto>> HandleAsync(
        string platform,
        string instanceId,
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        if (normalizedPlatform is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "platform 不合法");
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "instanceId 不能为空");
        }

        var config = await dbContext.ImConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.InstanceId == instanceId.Trim() &&
                        item.Platform == normalizedPlatform,
                cancellationToken);
        if (config is null || !string.Equals(config.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(404, "该实例未配置可用 IM");
        }

        var token = secretProtector.Unprotect(config.Token) ??
                    secretProtector.Unprotect(config.VerificationToken);
        if (!ValidateWebhookToken(token, headers))
        {
            logger.LogWarning(
                "IM webhook token validation failed. Platform={Platform}, InstanceId={InstanceId}",
                normalizedPlatform,
                instanceId);
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(401, "IM 签名校验失败");
        }

        var inbound = TryParseInboundMessage(payload);
        if (inbound is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "无法解析 IM 消息");
        }

        if (!string.IsNullOrWhiteSpace(inbound.ChatType) &&
            !string.Equals(inbound.ChatType, "p2p", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(inbound.ChatType, "single", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(inbound.ChatType, "private", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", null),
                "群聊消息已忽略");
        }

        if (string.IsNullOrWhiteSpace(inbound.Content))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", "暂不支持文件或非文本消息，请用文字描述"),
                "非文本消息已忽略");
        }

        var content = inbound.Content.Length > 4000 ? inbound.Content[..4000] : inbound.Content;
        if (inbound.Content.Length > 4000)
        {
            content += "\n\n[消息过长，已截断]";
        }

        var conversation = await runtimeConversationService.SendMessageAsync(
            instanceId,
            normalizedPlatform,
            content,
            config.OwnerUserId,
            inbound.MessageId,
            inbound.UserId,
            cancellationToken);
        if (!conversation.Success || conversation.Data?.AssistantMessage is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(
                conversation.Code,
                string.IsNullOrWhiteSpace(conversation.Message) ? "IM 消息处理失败" : conversation.Message);
        }

        return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
            new ImWebhookHandleResultDto("replied", conversation.Data.AssistantMessage.Content));
    }

    private static bool ValidateWebhookToken(string? expectedToken, IReadOnlyDictionary<string, string> headers)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return true;
        }

        var candidates = new[]
        {
            "X-HireBot-Verification-Token",
            "X-Lark-Request-Token",
            "X-Dingtalk-Token",
            "X-WeCom-Token"
        };
        foreach (var key in candidates)
        {
            if (headers.TryGetValue(key, out var value) &&
                string.Equals(value, expectedToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static InboundImMessage? TryParseInboundMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var content = FirstString(root, "content", "text");
            var messageId = FirstString(root, "message_id", "msg_id", "msgId");
            var userId = FirstString(root, "user_id", "open_id", "sender_id", "from_user_id");
            var chatType = FirstString(root, "chat_type", "conversation_type");

            if (root.TryGetProperty("event", out var eventElement))
            {
                content ??= FirstString(eventElement, "content", "text");
                messageId ??= FirstString(eventElement, "message_id", "msg_id", "msgId");
                userId ??= FirstString(eventElement, "user_id", "open_id", "sender_id", "from_user_id");
                chatType ??= FirstString(eventElement, "chat_type", "conversation_type");

                if (eventElement.TryGetProperty("message", out var messageElement))
                {
                    content ??= ExtractContent(messageElement);
                    messageId ??= FirstString(messageElement, "message_id", "msg_id", "msgId");
                    chatType ??= FirstString(messageElement, "chat_type", "conversation_type");
                }

                if (eventElement.TryGetProperty("sender", out var senderElement))
                {
                    userId ??= ExtractSenderId(senderElement);
                }
            }

            content ??= ExtractContent(root);
            return new InboundImMessage(
                string.IsNullOrWhiteSpace(messageId) ? BuildId("immsg") : messageId,
                string.IsNullOrWhiteSpace(userId) ? "unknown" : userId,
                content,
                chatType);
        }
        catch (JsonException)
        {
            return new InboundImMessage(BuildId("immsg"), "unknown", payload.Trim(), null);
        }
    }

    private static string? ExtractContent(JsonElement element)
    {
        var raw = FirstString(element, "content", "text");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var nested = JsonDocument.Parse(trimmed);
            return FirstString(nested.RootElement, "text", "content");
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static string? ExtractSenderId(JsonElement senderElement)
    {
        var direct = FirstString(senderElement, "user_id", "open_id", "sender_id");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (senderElement.TryGetProperty("sender_id", out var senderIdElement))
        {
            return FirstString(senderIdElement, "user_id", "open_id", "union_id");
        }

        return null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? NormalizePlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        var normalized = platform.Trim().ToLowerInvariant();
        return normalized is "feishu" or "dingtalk" or "wecom" ? normalized : null;
    }

    private static string BuildId(string prefix)
    {
        return $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];
    }

    private sealed record InboundImMessage(
        string MessageId,
        string UserId,
        string? Content,
        string? ChatType);
}

