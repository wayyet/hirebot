using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 瀹炰緥杩愯鏃跺璇濇湇鍔★紝澶勭悊瀹炰緥涓庣敤鎴蜂箣闂寸殑娑堟伅浜や簰銆?/// </summary>
public sealed class InstanceRuntimeConversationService(
    HireBotDbContext dbContext,
    IEmployeeRuntimeStore employeeStore,
    IRequestContextService requestContextService,
    ISandboxService sandboxService,
    ILogger<InstanceRuntimeConversationService> logger) : IInstanceRuntimeConversationService
{
    private const int ContextMessageLimit = 40;
    private const string RuntimeSandboxRole = "runtime";

    /// <summary>
    /// 鑾峰彇瀹炰緥鐨勮亰澶╂秷鎭垪琛ㄣ€?    /// </summary>
    /// <param name="instanceId">瀹炰緥ID</param>
    /// <param name="channel">娓犻亾绫诲瀷</param>
    /// <param name="ownerUserId">鎵€鏈夎€呯敤鎴稩D</param>
    /// <param name="limit">杩斿洖鏁伴噺闄愬埗</param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝</param>
    /// <returns>鑱婂ぉ鏃堕棿绾?/returns>
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
    /// 鍙戦€佹秷鎭粰瀹炰緥銆?    /// </summary>
    /// <param name="instanceId">瀹炰緥ID</param>
    /// <param name="channel">娓犻亾绫诲瀷</param>
    /// <param name="content">娑堟伅鍐呭</param>
    /// <param name="ownerUserId">鎵€鏈夎€呯敤鎴稩D</param>
    /// <param name="externalMessageId">澶栭儴娑堟伅ID</param>
    /// <param name="externalUserId">澶栭儴鐢ㄦ埛ID</param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝</param>
    /// <returns>鑱婂ぉ缁撴灉</returns>
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
            return ApiResponse<InstanceChatResultDto>.ErrorResponse(400, "content 涓嶈兘涓虹┖");
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

        var contextMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(item => item.ConversationId == conversation.ConversationId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(ContextMessageLimit)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new RuntimeChatMessageDto(item.Role, item.Content, item.CreatedAt))
            .ToArrayAsync(cancellationToken);

        var runtimeResponse = await SendSandboxRuntimeMessageAsync(
            access.Instance,
            access.OwnerSubject,
            access.Channel,
            conversation.ConversationId,
            content.Trim(),
            contextMessages,
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
    /// 娓呯┖瀹炰緥鐨勮亰澶╂秷鎭€?    /// </summary>
    /// <param name="instanceId">瀹炰緥ID</param>
    /// <param name="channel">娓犻亾绫诲瀷</param>
    /// <param name="ownerUserId">鎵€鏈夎€呯敤鎴稩D</param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝</param>
    /// <returns>鎿嶄綔缁撴灉</returns>
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
    /// 鑾峰彇鎴栧垱寤哄璇濆疄浣撱€?\r\n    /// </summary>
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
    /// 鍙戦€佽繍琛屾椂娑堟伅鍒?Sandbox銆?    /// </summary>
    private async Task<ApiResponse<HiringConversationResultDto>> SendSandboxRuntimeMessageAsync(
        InstanceEntity instance,
        string ownerSubject,
        string channel,
        string conversationId,
        string content,
        IReadOnlyList<RuntimeChatMessageDto> contextMessages,
        CancellationToken cancellationToken)
    {
        var (tenantId, operatorId) = requestContextService.ResolveTenantAndOperator(instance.TenantId, instance.OwnerUserId);
        var scopeKey = BuildRuntimeScopeKey(instance.InstanceId);
        var sandboxId = await ResolveRuntimeSandboxIdAsync(ownerSubject, scopeKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            var create = await sandboxService.CreateAsync(
                new SandboxCreateRequestDto
                {
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = scopeKey,
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject,
                    TenantId = tenantId,
                    OperatorId = operatorId,
                    ProvisioningMode = "managed",
                    UseCase = $"runtime-chat-for:{instance.InstanceId}"
                },
                cancellationToken);
            if (!create.Success || create.Data is null)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(create.Code, create.Message);
            }

            sandboxId = create.Data.SandboxId;
        }

        var history = string.Join(
           Environment.NewLine,
           contextMessages
               .TakeLast(12)
               .Select(message => $"{message.Role}: {message.Content}"));

        return await sandboxService.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = scopeKey,
                SandboxRole = RuntimeSandboxRole,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                SessionKey = channel,
                SandboxId = sandboxId,
                Content = history,
                Materials = [],
                UploadMaterialsAsAttachments = false
            },
            cancellationToken);
    }

    /// <summary>
    /// 瑙ｆ瀽杩愯鏃?Sandbox ID銆?    /// </summary>
    private async Task<string?> ResolveRuntimeSandboxIdAsync(string ownerSubject, string scopeKey, CancellationToken cancellationToken)
    {
        var sandbox = await dbContext.SandboxInstances
            .AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                item =>
                    item.OwnerSubject == ownerSubject &&
                    item.ScopeType == SandboxScopeTypes.Hire &&
                    item.ScopeKey == scopeKey &&
                    item.SandboxRole == RuntimeSandboxRole &&
                    item.State != "Deleted",
                cancellationToken);

        return sandbox?.SandboxId;
    }

    /// <summary>
    /// 鏋勫缓杩愯鏃朵綔鐢ㄥ煙閿€?    /// </summary>
    private static string BuildRuntimeScopeKey(string instanceId)
        => $"instance:{instanceId.Trim()}";

    /// <summary>
    /// 鏋勫缓杩愯鏃舵彁绀鸿瘝銆?\r\n    /// </summary>
    private async Task<AccessResult> ResolveAccessAsync(
        string instanceId,
        string channel,
        string? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return AccessResult.Fail(400, "instanceId 涓嶈兘涓虹┖");
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
            return AccessResult.Fail(409, "閮ㄩ棬鍛樺伐涓嶈兘鐩存帴浣滀负涓汉杩愯鏃跺璇濆璞★紝璇峰厛鍒涘缓涓汉鍒嗚韩");
        }

        if (!string.Equals(instance.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return AccessResult.Fail(403, "forbidden");
        }

        var employee = await employeeStore.FindAsync(normalizedInstanceId, cancellationToken);
        return AccessResult.Ok(instance, employee, owner, normalizedChannel);
    }

    /// <summary>
    /// 瑙勮寖鍖栨笭閬撳悕绉般€?    /// </summary>
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
    /// 鏋勫缓鍞竴 ID銆?    /// </summary>
    private static string BuildId(string prefix)
    {
        return $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];
    }

    /// <summary>
    /// 璁块棶缁撴灉銆?\r\n    /// </summary>
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
}

