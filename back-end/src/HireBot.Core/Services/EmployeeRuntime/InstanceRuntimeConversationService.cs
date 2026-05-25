using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例运行时对话服务，处理实例与用户之间的消息交互。
/// </summary>
public sealed class InstanceRuntimeConversationService(
    HireBotDbContext dbContext,
    IRequestContextService requestContextService,
    ISandboxService sandboxService,
    ILogger<InstanceRuntimeConversationService> logger) : IInstanceRuntimeConversationService
{
    private const string RuntimeSandboxRole = "runtime";

    /// <summary>
    /// 获取实例的聊天消息列表。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="channel">渠道类型</param>
    /// <param name="ownerUserId">所有者用户ID</param>
    /// <param name="limit">返回数量限制</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天时间线</returns>
    public async Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(
        string instanceId,
        string channel,
        string? ownerUserId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(instanceId, channel, ownerUserId, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<InstanceChatTimelineDto>.ErrorResponse(access.Code, access.Message);
        }

        var conversation = await GetOrCreateConversationAsync(access.Instance!, access.OwnerSubject, access.Channel, cancellationToken);
        var boundedLimit = Math.Clamp(limit, 1, 200);
        var messages = await dbContext.Messages
            .AsNoTracking()
            .Where(item => item.ConversationId == conversation.ConversationId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(boundedLimit)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new InstanceChatMessageDto(item.MessageId, item.Role, item.Content, item.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return ApiResponse<InstanceChatTimelineDto>.SuccessResponse(
            new InstanceChatTimelineDto(access.Instance!.InstanceId, conversation.ConversationId, messages));
    }

    /// <summary>
    /// 发送消息给实例。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="channel">渠道类型</param>
    /// <param name="content">消息内容</param>
    /// <param name="ownerUserId">所有者用户ID</param>
    /// <param name="externalMessageId">外部消息ID</param>
    /// <param name="externalUserId">外部用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天结果</returns>
    public async Task<ApiResponse<InstanceChatResultDto>> SendMessageAsync(
        string instanceId,
        string channel,
        string content,
        string? ownerUserId = null,
        string? externalMessageId = null,
        string? externalUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ApiResponse<InstanceChatResultDto>.ErrorResponse(400, "content 不能为空");
        }

        var access = await ResolveAccessAsync(instanceId, channel, ownerUserId, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<InstanceChatResultDto>.ErrorResponse(access.Code, access.Message);
        }

        if (!string.IsNullOrWhiteSpace(externalMessageId))
        {
            var exists = await dbContext.Messages.AnyAsync(
                item => item.Channel == access.Channel && item.ExternalMessageId == externalMessageId.Trim(),
                cancellationToken);
            if (exists)
            {
                return ApiResponse<InstanceChatResultDto>.ErrorResponse(409, "Duplicate IM message ignored.");
            }
        }

        var conversation = await GetOrCreateConversationAsync(access.Instance!, access.OwnerSubject, access.Channel, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var userMessage = new MessageEntity
        {
            MessageId = BuildId("msg"),
            ConversationId = conversation.ConversationId,
            InstanceId = access.Instance!.InstanceId,
            TenantId = access.Instance.TenantId,
            Role = "user",
            Content = content.Trim(),
            Channel = access.Channel,
            ExternalMessageId = string.IsNullOrWhiteSpace(externalMessageId) ? null : externalMessageId.Trim(),
            ExternalUserId = string.IsNullOrWhiteSpace(externalUserId) ? null : externalUserId.Trim(),
            DeliveryStatus = "received",
            CreatedAt = now
        };

        dbContext.Messages.Add(userMessage);
        conversation.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var runtimeResponse = await SendSandboxRuntimeMessageAsync(
            access.Instance,
            access.OwnerSubject,
            access.Channel,
            conversation.ConversationId,
            content.Trim(),
            cancellationToken);
        if (!runtimeResponse.Success || runtimeResponse.Data is null || runtimeResponse.Data.AssistantMessage is null)
        {
            return ApiResponse<InstanceChatResultDto>.ErrorResponse(runtimeResponse.Code, runtimeResponse.Message);
        }

        var assistantMessage = new MessageEntity
        {
            MessageId = BuildId("msg"),
            ConversationId = conversation.ConversationId,
            InstanceId = access.Instance.InstanceId,
            TenantId = access.Instance.TenantId,
            Role = "assistant",
            Content = runtimeResponse.Data.AssistantMessage.Content.Trim(),
            Channel = access.Channel,
            DeliveryStatus = "generated",
            CreatedAt = DateTimeOffset.UtcNow
        };

        conversation.UpdatedAt = assistantMessage.CreatedAt;
        dbContext.Messages.Add(assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<InstanceChatResultDto>.SuccessResponse(
            new InstanceChatResultDto(
                access.Instance.InstanceId,
                conversation.ConversationId,
                new InstanceChatMessageDto(
                    assistantMessage.MessageId,
                    assistantMessage.Role,
                    assistantMessage.Content,
                    assistantMessage.CreatedAt)));
    }

    /// <summary>
    /// 清空实例的聊天消息。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="channel">渠道类型</param>
    /// <param name="ownerUserId">所有者用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<ApiResponse<bool>> ClearMessagesAsync(
        string instanceId,
        string channel,
        string? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(instanceId, channel, ownerUserId, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<bool>.ErrorResponse(access.Code, access.Message);
        }

        var conversations = await dbContext.Conversations
            .Where(item =>
                item.InstanceId == access.Instance!.InstanceId &&
                item.OwnerUserId == access.OwnerSubject &&
                item.Channel == access.Channel)
            .ToArrayAsync(cancellationToken);
        if (conversations.Length == 0)
        {
            return ApiResponse<bool>.SuccessResponse(true, "Conversation cleared.");
        }

        var conversationIds = conversations.Select(item => item.ConversationId).ToArray();
        var messages = await dbContext.Messages
            .Where(item => conversationIds.Contains(item.ConversationId))
            .ToArrayAsync(cancellationToken);
        dbContext.Messages.RemoveRange(messages);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Conversation cleared.");
    }

    /// <summary>
    /// 获取或创建对话实体。
    /// </summary>
    private async Task<ConversationEntity> GetOrCreateConversationAsync(
        InstanceEntity instance,
        string ownerSubject,
        string channel,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.FirstOrDefaultAsync(
            item =>
                item.InstanceId == instance.InstanceId &&
                item.OwnerUserId == ownerSubject &&
                item.Channel == channel,
            cancellationToken);
        if (conversation is not null)
        {
            return conversation;
        }

        conversation = new ConversationEntity
        {
            ConversationId = BuildId("conv"),
            InstanceId = instance.InstanceId,
            TenantId = instance.TenantId,
            OwnerUserId = ownerSubject,
            Channel = channel,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    /// <summary>
    /// 发送运行时消息到 Sandbox。
    /// </summary>
    private async Task<ApiResponse<HiringConversationResultDto>> SendSandboxRuntimeMessageAsync(
        InstanceEntity instance,
        string ownerSubject,
        string channel,
        string conversationId,
        string content,
        CancellationToken cancellationToken)
    {
        var (tenantId, operatorId) = requestContextService.ResolveTenantAndOperator(instance.TenantId, instance.OwnerUserId);
        var scopeKey = BuildRuntimeScopeKey(instance.InstanceId);
        var sandboxId = await ResolveRuntimeSandboxIdAsync(ownerSubject, scopeKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            // DB 中无记录（沙箱不存在或已被标记删除），自动重建。
            // PVC 按 scopeKey 持久保留，重建容器后工作区数据自动恢复，无需重新上传技能包。
            logger.LogWarning(
                "Runtime sandbox not found, auto-recreating before send. InstanceId={InstanceId} ScopeKey={ScopeKey}",
                instance.InstanceId, scopeKey);

            var create = await sandboxService.CreateAsync(
                new SandboxCreateRequestDto
                {
                    ScopeType = SandboxScopeTypes.Runtime,
                    ScopeKey = scopeKey,
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject,
                    TenantId = tenantId,
                    OperatorId = operatorId,
                    ProvisioningMode = "managed",
                    UseCase = $"runtime-chat-for:{instance.InstanceId}",
                    Metadata = BuildRuntimeSandboxMeta(ownerSubject, instance.InstanceId, instance.BasedOnTemplateId)
                },
                cancellationToken);
            if (!create.Success || create.Data is null)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(create.Code, create.Message);
            }

            // 等待沙箱就绪（最多 180 秒），确保网关可用后再发送消息
            for (var attempt = 0; attempt < 36; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                var refresh = await sandboxService.RefreshAsync(
                    new SandboxInstanceLookupRequestDto { SandboxId = create.Data.SandboxId },
                    cancellationToken);
                if (refresh.Success && refresh.Data is not null &&
                    string.Equals(refresh.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(refresh.Data.GatewayEndpoint))
                {
                    sandboxId = create.Data.SandboxId;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(sandboxId))
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(504, "runtime sandbox 自动重建超时，请稍后重试");
            }
        }

        return await sandboxService.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Runtime,
                ScopeKey = scopeKey,
                SandboxRole = RuntimeSandboxRole,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                SessionKey = channel,
                SandboxId = sandboxId,
                Content = content,
                Materials = [],
                UploadMaterialsAsAttachments = false
            },
            cancellationToken);
    }

    /// <summary>
    /// 解析运行时 Sandbox ID。
    /// </summary>
    private async Task<string?> ResolveRuntimeSandboxIdAsync(string ownerSubject, string scopeKey, CancellationToken cancellationToken)
    {
        var sandbox = await dbContext.SandboxInstances
            .AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                item =>
                    item.OwnerSubject == ownerSubject &&
                    item.ScopeType == SandboxScopeTypes.Runtime &&
                    item.ScopeKey == scopeKey &&
                    item.SandboxRole == RuntimeSandboxRole &&
                    item.State != "Deleted",
                cancellationToken);

        return sandbox?.SandboxId;
    }

    /// <summary>
    /// 构建运行时作用域密钥。
    /// </summary>
    private static string BuildRuntimeScopeKey(string instanceId)
        => $"instance:{instanceId.Trim()}";

    /// <summary>
    /// 构建运行时提供令牌。
    /// </summary>
    private async Task<AccessResult> ResolveAccessAsync(
        string instanceId,
        string channel,
        string? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return AccessResult.Fail(400, "instanceId 不能为空");
        }

        var normalizedChannel = NormalizeChannel(channel);
        if (normalizedChannel is null)
        {
            return AccessResult.Fail(400, "channel is invalid");
        }

        var normalizedInstanceId = instanceId.Trim();
        var owner = string.IsNullOrWhiteSpace(ownerUserId)
            ? requestContextService.ResolveOwnerSubject()
            : ownerUserId.Trim();

        var instance = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == normalizedInstanceId, cancellationToken);
        if (instance is null)
        {
            return AccessResult.Fail(404, "instance not found");
        }

        if (!string.Equals(instance.Status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return AccessResult.Fail(409, "only live instances can be used for runtime chat");
        }

        var isPersonalRuntime = string.Equals(instance.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(instance.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase);
        if (!isPersonalRuntime)
        {
            return AccessResult.Fail(409, "部门员工不能直接作为个人运行时对话对象，请先创建个人分身");
        }

        if (!string.Equals(instance.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return AccessResult.Fail(403, "forbidden");
        }

        EmployeeDetailDto? employee = null;
        if (!string.IsNullOrWhiteSpace(instance.RuntimeSnapshotJson))
        {
            try { employee = JsonSerializer.Deserialize<EmployeeDetailDto>(instance.RuntimeSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch { /* ignore deserialize failure */ }
        }
        return AccessResult.Ok(instance, employee, owner, normalizedChannel);
    }

    /// <summary>
    /// 规范化频道名称。
    /// </summary>
    private static string? NormalizeChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        var normalized = channel.Trim().ToLowerInvariant();
        return normalized is "inapp" or "feishu" or "dingtalk" or "wecom" ? normalized : null;
    }

    /// <summary>
    /// 构建唯一 ID。
    /// </summary>
    private static string BuildId(string prefix)
    {
        return $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];
    }

    /// <summary>
    /// 访问结果。
    /// </summary>
    private sealed record AccessResult(
        bool Success,
        int Code,
        string Message,
        InstanceEntity? Instance,
        EmployeeDetailDto? Employee,
        string OwnerSubject,
        string Channel)
    {
        public static AccessResult Ok(InstanceEntity instance, EmployeeDetailDto? employee, string ownerSubject, string channel)
        {
            return new AccessResult(true, 200, string.Empty, instance, employee, ownerSubject, channel);
        }

        public static AccessResult Fail(int code, string message)
        {
            return new AccessResult(false, code, message, null, null, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// 构建个人运行时沙箱元数据。
    /// </summary>
    private static Dictionary<string, string> BuildRuntimeSandboxMeta(
        string ownerSubject, string instanceId, string? templateId)
    {
        var meta = new Dictionary<string, string>
        {
            [SandboxMetaKeys.UserSubject] = ownerSubject,
            [SandboxMetaKeys.InstanceId] = instanceId
        };
        if (!string.IsNullOrWhiteSpace(templateId))
            meta[SandboxMetaKeys.TemplateId] = templateId;
        return meta;
    }
}