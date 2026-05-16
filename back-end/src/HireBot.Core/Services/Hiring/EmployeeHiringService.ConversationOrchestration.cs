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

    private Task<HiringRuntimeContext?> RefreshRuntimeProgressAsync(string hireId, CancellationToken cancellationToken)
    {
        var runtimeContext = hiringRuntimeStore.Get(hireId);
        if (runtimeContext is null)
        {
            return Task.FromResult<HiringRuntimeContext?>(null);
        }

        runtimeContext = ApplyWorkflowProgress(runtimeContext with
        {
            StructuredData = NormalizeStructuredData(runtimeContext.StructuredData)
        });
        hiringRuntimeStore.Upsert(runtimeContext);

        return Task.FromResult<HiringRuntimeContext?>(runtimeContext);
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

        // 同步 SandboxId（沙箱可能被基础设施重建后 ID 更新）
        if (!string.Equals(runtimeContext.SandboxId, refreshResult.Data.SandboxId, StringComparison.Ordinal))
        {
            runtimeContext = runtimeContext with { SandboxId = refreshResult.Data.SandboxId };
            hiringRuntimeStore.Upsert(runtimeContext);
        }

        // 前端通过 WS 直连负责模板上传与引导，后端不再执行 priming；若未标记已初始化则补标
        if (!refreshResult.Data.IsInitialized)
        {
            logger.LogInformation(
                "Sandbox not yet initialized, marking as initialized (frontend-driven bootstrap). HireId={HireId}, SandboxId={SandboxId}",
                runtimeContext.HireId,
                refreshResult.Data.SandboxId);
            await SetSandboxInitializedAsync(refreshResult.Data.SandboxId, cancellationToken);
        }

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

}
