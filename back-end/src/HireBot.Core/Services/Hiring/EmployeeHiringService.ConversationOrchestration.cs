using System.Collections.Concurrent;
using System.IO.Compression;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    private Task<ApiResponse<HiringConversationControlResultDto>> SetConversationPausedAsync(
        string hireId,
        bool isPaused)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return Task.FromResult(ApiResponse<HiringConversationControlResultDto>.ErrorResponse(400, error));
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is null)
        {
            return Task.FromResult(ApiResponse<HiringConversationControlResultDto>.ErrorResponse(404, "雇佣上下文不存在，请重新发起流程"));
        }

        runtimeContext = runtimeContext with
        {
            IsConversationPaused = isPaused
        };
        hiringRuntimeStore.Upsert(runtimeContext);

        var result = new HiringConversationControlResultDto(
            HireId: runtimeContext.HireId,
            CurrentStage: runtimeContext.CurrentStage,
            CollectionPhase: runtimeContext.CollectionPhase,
            IsConversationPaused: runtimeContext.IsConversationPaused,
            IsConversationResponding: IsConversationResponding(normalizedHireId, runtimeContext));

        var message = isPaused ? "对话已暂停" : "对话已恢复";
        return Task.FromResult(ApiResponse<HiringConversationControlResultDto>.SuccessResponse(result, message));
    }

    private bool IsConversationResponding(string hireId, HiringRuntimeContext? runtimeContext = null)
    {
        return conversationInFlight.ContainsKey(hireId) || runtimeContext?.IsConversationResponding == true;
    }

    private async Task<HiringRuntimeContext?> RefreshRuntimeProgressAsync(string hireId, CancellationToken cancellationToken)
    {
        var runtimeContext = hiringRuntimeStore.Get(hireId);
        if (runtimeContext is null)
        {
            return null;
        }

        runtimeContext = await RefreshHandoffStateFromSandboxAsync(runtimeContext, cancellationToken);
        runtimeContext = ApplyWorkflowProgress(runtimeContext with
        {
            StructuredData = NormalizeStructuredData(runtimeContext.StructuredData)
        });
        hiringRuntimeStore.Upsert(runtimeContext);

        return runtimeContext;
    }

    private async Task<HiringRuntimeContext?> EnsureSandboxReinitializedAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeContext.RoleTemplatePackage.PackageId))
        {
            return runtimeContext;
        }

        var refreshResult = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto
            {
                SandboxId = runtimeContext.SandboxId,
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = runtimeContext.HireId,
                SandboxRole = ResolveSandboxRole(runtimeContext.HireId),
                OwnerSubject = runtimeContext.OwnerSubject,
                TenantId = runtimeContext.TenantId,
                OperatorId = runtimeContext.OperatorId,
                TemplateId = runtimeContext.TemplateId
            },
            cancellationToken);

        if (!refreshResult.Success || refreshResult.Data is null)
        {
            logger.LogWarning(
                "Sandbox re-initialization skipped: RefreshAsync failed. HireId={HireId}, Error={Error}",
                runtimeContext.HireId,
                refreshResult.Message);
            return runtimeContext;
        }

        if (refreshResult.Data.IsInitialized)
        {
            if (!string.Equals(runtimeContext.SandboxId, refreshResult.Data.SandboxId, StringComparison.Ordinal))
            {
                runtimeContext = runtimeContext with { SandboxId = refreshResult.Data.SandboxId };
                hiringRuntimeStore.Upsert(runtimeContext);
            }

            return runtimeContext;
        }

        logger.LogInformation(
            "Sandbox re-initialization started. HireId={HireId}, SandboxId={SandboxId}",
            runtimeContext.HireId,
            refreshResult.Data.SandboxId);

        runtimeContext = runtimeContext with { SandboxId = refreshResult.Data.SandboxId };
        hiringRuntimeStore.Upsert(runtimeContext);

        var templatePackageCall = await UploadTemplatePackageAsync(
            runtimeContext.HireId,
            runtimeContext.RoleTemplatePackage,
            runtimeContext.OwnerSubject,
            cancellationToken);
        if (!templatePackageCall.Success || templatePackageCall.Data is null)
        {
            logger.LogWarning(
                "Sandbox re-initialization: template upload failed. HireId={HireId}, Error={Error}",
                runtimeContext.HireId,
                templatePackageCall.Message);
            return runtimeContext;
        }

        EmployeeTemplateDefinition template;
        try
        {
            template = await templateDataProvider.GetByIdAsync(runtimeContext.TemplateId, cancellationToken)
                ?? new EmployeeTemplateDefinition(
                    TemplateId: runtimeContext.TemplateId,
                    IconUrl: string.Empty,
                    Name: runtimeContext.TemplateName,
                    Tagline: string.Empty,
                    Description: string.Empty,
                    DetailDoc: string.Empty,
                    CoreAbilityTags: [],
                    HiredCount: 0,
                    SuccessRate: 0m,
                    AvgRating: 0m,
                    IsAvailable: true,
                    CoreAbilities: [],
                    InScope: [],
                    OutOfScope: [],
                    Prerequisites: [],
                    SuccessCases: []);
        }
        catch
        {
            template = new EmployeeTemplateDefinition(
                TemplateId: runtimeContext.TemplateId,
                IconUrl: string.Empty,
                Name: runtimeContext.TemplateName,
                Tagline: string.Empty,
                Description: string.Empty,
                DetailDoc: string.Empty,
                CoreAbilityTags: [],
                HiredCount: 0,
                SuccessRate: 0m,
                AvgRating: 0m,
                IsAvailable: true,
                CoreAbilities: [],
                InScope: [],
                OutOfScope: [],
                Prerequisites: [],
                SuccessCases: []);
        }

        var primingContent = BuildReferenceTemplatePrimingContent(
            template,
            runtimeContext.ReferenceTemplatePackage,
            LoadReferenceTemplatePrimingPrompt());

        var existingSession = await dbContext.HiringSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.HireId == runtimeContext.HireId, cancellationToken);
        PersistedSourceZipInfo? referenceSourceZip = null;
        if (existingSession is not null
            && !string.IsNullOrWhiteSpace(existingSession.SourceZipStoragePath)
            && !string.IsNullOrWhiteSpace(existingSession.SourceZipSha256))
        {
            referenceSourceZip = new PersistedSourceZipInfo(
                existingSession.SourceZipStoragePath
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault() ?? "source.zip",
                existingSession.SourceZipStoragePath,
                existingSession.SourceZipSha256,
                existingSession.SourceZipSizeBytes ?? 0);
        }

        var primingMaterials = BuildReferenceTemplatePrimingMaterials(referenceSourceZip);
        var primingResponse = await SendInternalPrimingMessageAsync(
            runtimeContext,
            primingContent,
            primingMaterials,
            cancellationToken);
        if (!primingResponse.Success || primingResponse.Data is null)
        {
            logger.LogWarning(
                "Sandbox re-initialization: priming failed. HireId={HireId}, Error={Error}",
                runtimeContext.HireId,
                primingResponse.Message);
            return runtimeContext;
        }

        await SetSandboxInitializedAsync(refreshResult.Data.SandboxId, cancellationToken);

        logger.LogInformation(
            "Sandbox re-initialization completed. HireId={HireId}, SandboxId={SandboxId}",
            runtimeContext.HireId,
            runtimeContext.SandboxId);

        return runtimeContext;
    }

    private async Task<ApiResponse<HiringConversationResultDto>> SendSandboxConversationMessageAsync(
        HiringRuntimeContext runtimeContext,
        string content,
        IReadOnlyList<HiringConversationMaterialDto> materials,
        CancellationToken cancellationToken)
    {
        return await sandboxService.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = runtimeContext.HireId,
                SandboxRole = ResolveSandboxRole(runtimeContext.HireId),
                OwnerSubject = runtimeContext.OwnerSubject,
                TenantId = runtimeContext.TenantId,
                OperatorId = runtimeContext.OperatorId,
                SessionKey = "default",
                SandboxId = runtimeContext.SandboxId,
                Content = content?.Trim() ?? string.Empty,
                StructuredAnswers = null,
                Materials = materials,
                UploadMaterialsAsAttachments = materials.Count > 0
            },
            cancellationToken);
    }

    private async Task<ApiResponse<HiringConversationResultDto>> SendInternalPrimingMessageAsync(
        HiringRuntimeContext runtimeContext,
        string content,
        IReadOnlyList<HiringConversationMaterialDto> materials,
        CancellationToken cancellationToken)
    {
        var sendResponse = await SendSandboxConversationMessageAsync(
            runtimeContext, content, materials, cancellationToken);
        if (!sendResponse.Success || sendResponse.Data is null)
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(sendResponse.Code, sendResponse.Message);
        }

        runtimeContext = runtimeContext with { SessionId = sendResponse.Data.SessionId };
        return await ProcessConversationTurnAsync(
            runtimeContext,
            userMessageContent: null,
            sendResponse.Data.AssistantMessage.Content,
            materials,
            structuredAnswers: null,
            cancellationToken);
    }

    /// <summary>
    /// 处理已完成的对话轮次（不负责将消息发送到沙箱）。
    /// 解析 AI 回复中的结构化标签，推进工作流状态，持久化运行时上下文。
    /// </summary>
    private async Task<ApiResponse<HiringConversationResultDto>> ProcessConversationTurnAsync(
        HiringRuntimeContext runtimeContext,
        string? userMessageContent,
        string rawAssistantReply,
        IReadOnlyList<HiringConversationMaterialDto> materials,
        IReadOnlyDictionary<string, string?>? structuredAnswers,
        CancellationToken cancellationToken)
    {
        var parsedReply = HiringWorkflowSupport.ParseAssistantReply(rawAssistantReply);
        LogParsedAssistantReply(runtimeContext, parsedReply);
        var now = DateTimeOffset.UtcNow;
        var assistantMessage = new HiringConversationMessageDto(
            $"assistant-{Guid.NewGuid():N}",
            "assistant",
            rawAssistantReply,
            now);
        var visibleAssistantMessage = assistantMessage with { Content = parsedReply.VisibleContent };

        var messages = runtimeContext.Messages;
        if (!string.IsNullOrWhiteSpace(userMessageContent))
        {
            messages = AppendMessages(
                messages,
                new HiringConversationMessageDto(
                    $"user-{Guid.NewGuid():N}",
                    "user",
                    userMessageContent.Trim(),
                    now));
        }
        messages = AppendMessages(messages, visibleAssistantMessage);

        runtimeContext = runtimeContext with
        {
            Materials = MergeMaterials(runtimeContext.Materials, materials),
            Messages = messages,
            StructuredData = structuredAnswers is not null
                ? MergeStructuredData(runtimeContext.StructuredData, structuredAnswers)
                : runtimeContext.StructuredData
        };

        runtimeContext = await RefreshHandoffStateFromSandboxAsync(runtimeContext, cancellationToken);
        runtimeContext = ApplyAssistantReply(runtimeContext, parsedReply);
        runtimeContext = ApplyDispatchCallbacks(runtimeContext, parsedReply.DispatchCallbacks);
        runtimeContext = await ExecuteDispatchCommandsAsync(runtimeContext, parsedReply.DispatchCommands, cancellationToken);
        runtimeContext = ApplyWorkflowProgress(runtimeContext);
        runtimeContext = ApplyConversationProgressToTemplatePackage(runtimeContext);
        if (ShouldPersistArtifactPackages(runtimeContext))
        {
            await PersistIntermediatePackageAsync(runtimeContext, cancellationToken);
        }

        hiringRuntimeStore.Upsert(runtimeContext);
        var latestPreview = BuildLocalStagePreview(
            runtimeContext.HireId,
            runtimeContext.DiscoverySkill,
            runtimeContext.StageCompletion,
            runtimeContext.CurrentStage,
            runtimeContext.CollectionPhase,
            runtimeContext.StructuredData,
            visibleAssistantMessage.Content);

        return ApiResponse<HiringConversationResultDto>.SuccessResponse(
            new HiringConversationResultDto(
                runtimeContext.HireId,
                runtimeContext.SessionId,
                runtimeContext.CurrentStage,
                latestPreview.ReadyForAudit,
                visibleAssistantMessage,
                latestPreview,
                runtimeContext.IsConversationPaused,
                true));
    }

    private async Task<HiringRuntimeContext> RefreshHandoffStateFromSandboxAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeContext.SessionId))
        {
            return runtimeContext;
        }

        var sessionDetailResult = await sandboxService.GetSessionDetailAsync(
            new SandboxSessionDetailRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = runtimeContext.HireId,
                SandboxRole = ResolveSandboxRole(runtimeContext.HireId),
                OwnerSubject = runtimeContext.OwnerSubject,
                TenantId = runtimeContext.TenantId,
                OperatorId = runtimeContext.OperatorId,
                SessionKey = "default",
                SandboxId = runtimeContext.SandboxId
            },
            cancellationToken);
        if (!sessionDetailResult.Success || sessionDetailResult.Data is null)
        {
            logger.LogWarning("无法刷新会话 {SessionId} 的 handoff 元数据: {Message}", runtimeContext.SessionId, sessionDetailResult.Message);
            return runtimeContext;
        }

        var sandboxHandoffCount = sessionDetailResult.Data.HandoffItems.Count;
        var projectedHandoffItems = ProjectHandoffItems(sessionDetailResult.Data.HandoffItems);
        logger.LogInformation(
            "RefreshHandoffStateFromSandbox: SessionId={SessionId}, SandboxHandoffCount={SandboxCount}, ProjectedCount={ProjectedCount}",
            runtimeContext.SessionId,
            sandboxHandoffCount,
            projectedHandoffItems.Count);

        return runtimeContext with
        {
            SessionId = sessionDetailResult.Data.SessionId,
            HandoffItems = projectedHandoffItems
        };
    }

    private static IReadOnlyList<HiringWorkflowHandoffDto> ProjectHandoffItems(
        IReadOnlyList<SandboxSessionHandoffItemDto> handoffItems)
    {
        if (handoffItems.Count == 0)
        {
            return [];
        }

        return handoffItems
            .Select(ProjectHandoffItem)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.HandoffId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HiringWorkflowHandoffDto ProjectHandoffItem(SandboxSessionHandoffItemDto handoffItem)
    {
        var handoffId = RequireHandoffField(handoffItem.HandoffId, nameof(handoffItem.HandoffId), handoffItem.HandoffId);
        var createdAtUtc = handoffItem.CreatedAtUtc == default ? DateTimeOffset.UtcNow : handoffItem.CreatedAtUtc;
        var updatedAtUtc = handoffItem.UpdatedAtUtc == default ? createdAtUtc : handoffItem.UpdatedAtUtc;

        return new HiringWorkflowHandoffDto(
            SessionId: RequireHandoffField(handoffId, nameof(handoffItem.SessionId), handoffItem.SessionId),
            WorkflowId: RequireHandoffField(handoffId, nameof(handoffItem.WorkflowId), handoffItem.WorkflowId),
            HandoffId: handoffId,
            Title: RequireHandoffField(handoffId, nameof(handoffItem.Title), handoffItem.Title),
            Kind: NormalizeRequiredHandoffKind(handoffId, handoffItem.Kind),
            Stage: NormalizeRequiredHandoffStage(handoffId, handoffItem.Stage),
            TargetSkill: RequireHandoffField(handoffId, nameof(handoffItem.TargetSkill), handoffItem.TargetSkill),
            Intent: TrimOrNull(handoffItem.Intent),
            Category: TrimOrNull(handoffItem.Category),
            Payload: CloneHandoffPayloadOrEmpty(handoffItem.Payload),
            Source: TrimOrNull(handoffItem.Source),
            Acceptance: TrimOrNull(handoffItem.Acceptance),
            Status: NormalizeRequiredHandoffStatus(handoffId, handoffItem.Status),
            Fingerprint: RequireHandoffField(handoffId, nameof(handoffItem.Fingerprint), handoffItem.Fingerprint),
            RelatedHandoffIds: handoffItem.RelatedHandoffIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RelatedFiles: handoffItem.RelatedFiles
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Revision: Math.Max(1, handoffItem.Revision),
            CreatedAtUtc: createdAtUtc,
            UpdatedAtUtc: updatedAtUtc,
            DispatchId: TrimOrNull(handoffItem.DispatchId),
            CallbackSummary: TrimOrNull(handoffItem.CallbackSummary));
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string RequireHandoffField(string handoffId, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Handoff {handoffId} 缺少必填字段 {fieldName}。");
        }

        return value.Trim();
    }

    private static string NormalizeRequiredHandoffKind(string handoffId, string? value)
    {
        return RequireHandoffField(handoffId, nameof(HiringWorkflowHandoffDto.Kind), value).Trim().ToLowerInvariant() switch
        {
            HiringHandoffKind.HandoffTodo => HiringHandoffKind.HandoffTodo,
            _ => throw new InvalidOperationException($"Handoff {handoffId} 的 kind 非法: {value}")
        };
    }

    private static string NormalizeRequiredHandoffStage(string handoffId, string? value)
    {
        return RequireHandoffField(handoffId, nameof(HiringWorkflowHandoffDto.Stage), value).Trim().ToLowerInvariant() switch
        {
            "material" => HiringCollectionStage.Material,
            "skill" => HiringCollectionStage.Skill,
            "external" => HiringCollectionStage.External,
            "ready_for_packaging" => HiringCollectionStage.ReadyForPackaging,
            "cross_stage" or "cross-stage" => "cross_stage",
            _ => throw new InvalidOperationException($"Handoff {handoffId} 的 stage 非法: {value}")
        };
    }

    private static string NormalizeRequiredHandoffStatus(string handoffId, string? value)
    {
        return RequireHandoffField(handoffId, nameof(HiringWorkflowHandoffDto.Status), value).Trim().ToLowerInvariant() switch
        {
            HiringHandoffStatus.Drafting => HiringHandoffStatus.Drafting,
            HiringHandoffStatus.ReadyToDispatch => HiringHandoffStatus.ReadyToDispatch,
            HiringHandoffStatus.Dispatched => HiringHandoffStatus.Dispatched,
            HiringHandoffStatus.Dirty => HiringHandoffStatus.Dirty,
            HiringHandoffStatus.Confirmed => HiringHandoffStatus.Confirmed,
            HiringHandoffStatus.NeedsReview => HiringHandoffStatus.NeedsReview,
            HiringHandoffStatus.Dismissed => HiringHandoffStatus.Dismissed,
            _ => throw new InvalidOperationException($"Handoff {handoffId} 的 status 非法: {value}")
        };
    }

    private static JsonElement CloneHandoffPayloadOrEmpty(JsonElement payload)
    {
        return payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? JsonSerializer.SerializeToElement(new Dictionary<string, object?>())
            : payload.Clone();
    }

}
