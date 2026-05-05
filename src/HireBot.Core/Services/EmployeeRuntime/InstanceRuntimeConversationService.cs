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
using System.IO.Compression;
using System.Security.Cryptography;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class InstanceRuntimeConversationService(
    HireBotDbContext dbContext,
    IEmployeeRuntimeStore employeeStore,
    IRequestContextService requestContextService,
    IInstanceArtifactResolver artifactResolver,
    ISandboxService sandboxService,
    ILogger<InstanceRuntimeConversationService> logger) : IInstanceRuntimeConversationService
{
    private const int ContextMessageLimit = 40;
    private const string RuntimeSandboxRole = "runtime";

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
                return ApiResponse<InstanceChatResultDto>.ErrorResponse(409, "重复的 IM 消息已忽略");
            }
        }

        InstanceArtifactResolution artifact;
        try
        {
            artifact = await artifactResolver.ResolveAsync(access.Instance!, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve instance artifacts. InstanceId={InstanceId}", access.Instance!.InstanceId);
            return ApiResponse<InstanceChatResultDto>.ErrorResponse(409, "实例五件套未就绪，无法对话");
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
            artifact,
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
            return ApiResponse<bool>.SuccessResponse(true, "对话已清空");
        }

        var conversationIds = conversations.Select(item => item.ConversationId).ToArray();
        var messages = await dbContext.Messages
            .Where(item => conversationIds.Contains(item.ConversationId))
            .ToArrayAsync(cancellationToken);
        dbContext.Messages.RemoveRange(messages);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "对话已清空");
    }

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

    private async Task<ApiResponse<HiringConversationResultDto>> SendSandboxRuntimeMessageAsync(
        InstanceEntity instance,
        string ownerSubject,
        string channel,
        string conversationId,
        string content,
        InstanceArtifactResolution artifact,
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

        var artifactArchivePath = BuildArtifactArchive(instance, artifact.ArtifactRoot);
        try
        {
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
                    Content = BuildRuntimePrompt(instance, channel, conversationId, content, artifact, contextMessages),
                    Materials =
                    [
                        new HiringConversationMaterialDto
                        {
                            Type = "file",
                            Name = $"{instance.InstanceId}-{instance.CurrentVersion}-artifacts.zip",
                            ContentHash = ComputeFileHash(artifactArchivePath),
                            Size = new FileInfo(artifactArchivePath).Length,
                            MimeType = "application/zip",
                            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["storagePath"] = artifactArchivePath,
                                ["artifactRoot"] = artifact.ArtifactRoot,
                                ["instanceId"] = instance.InstanceId,
                                ["instanceType"] = instance.InstanceType,
                                ["currentVersion"] = instance.CurrentVersion
                            }
                        }
                    ],
                    UploadMaterialsAsAttachments = true
                },
                cancellationToken);
        }
        finally
        {
            TryDeleteFile(artifactArchivePath);
        }
    }

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

    private static string BuildRuntimeScopeKey(string instanceId)
        => $"instance:{instanceId.Trim()}";

    private static string BuildRuntimePrompt(
        InstanceEntity instance,
        string channel,
        string conversationId,
        string content,
        InstanceArtifactResolution artifact,
        IReadOnlyList<RuntimeChatMessageDto> contextMessages)
    {
        var history = string.Join(
            Environment.NewLine,
            contextMessages
                .TakeLast(12)
                .Select(message => $"{message.Role}: {message.Content}"));

        return $"""
你是已上岗数字员工实例的运行时助手。请基于随消息上传的五件套附件、实例元数据和对话历史，直接完成用户请求。

instance_id: {instance.InstanceId}
instance_type: {instance.InstanceType}
current_version: {instance.CurrentVersion}
from_instance_id: {instance.FromInstanceId ?? string.Empty}
channel: {channel}
conversation_id: {conversationId}
artifact_root: {artifact.ArtifactRoot}

recent_messages:
{history}

user_message:
{content}
""";
    }

    private static string BuildArtifactArchive(InstanceEntity instance, string artifactRoot)
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"hirebot-{instance.InstanceId}-{Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(artifactRoot, archivePath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return archivePath;
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for transient runtime artifact archives.
        }
    }

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
            return AccessResult.Fail(400, "channel 不合法");
        }

        var normalizedInstanceId = instanceId.Trim();
        var owner = string.IsNullOrWhiteSpace(ownerUserId)
            ? requestContextService.ResolveOwnerSubject()
            : ownerUserId.Trim();

        var instance = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == normalizedInstanceId, cancellationToken);
        if (instance is null)
        {
            return AccessResult.Fail(404, "实例不存在");
        }

        if (!string.Equals(instance.Status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return AccessResult.Fail(409, "只有已上岗实例可以对话");
        }

        var isPersonalRuntime = string.Equals(instance.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(instance.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase);
        if (!isPersonalRuntime)
        {
            return AccessResult.Fail(409, "部门员工不能直接作为个人运行时对话对象，请先创建个人分身");
        }

        if (!string.Equals(instance.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return AccessResult.Fail(403, "无权访问该实例对话");
        }

        var employee = await employeeStore.FindAsync(normalizedInstanceId, cancellationToken);
        return AccessResult.Ok(instance, employee, owner, normalizedChannel);
    }

    private static string? NormalizeChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        var normalized = channel.Trim().ToLowerInvariant();
        return normalized is "inapp" or "feishu" or "dingtalk" or "wecom" ? normalized : null;
    }

    private static string BuildId(string prefix)
    {
        return $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];
    }

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

