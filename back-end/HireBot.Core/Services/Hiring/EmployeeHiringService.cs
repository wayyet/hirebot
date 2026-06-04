using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Infrastructure.Identity;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 雇佣服务（简化版：基于轻量表设计，移除 HiringRuntimeContext）。
/// </summary>
internal sealed class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    ISandboxService sandboxService,
    IHiringStageService hiringStageService,
    IUserIdentity userIdentity,
    HireBotDbContext dbContext,
    IMemoryCache memoryCache,
    ILogger<EmployeeHiringService> logger) : IEmployeeHiringService
{
    public async Task<ApiResponse<HireTemplateResultDto>> HireAsync(
        string templateId,
        string? useCase = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(400, "templateId 不能为空");
        }

        var tenantId = userIdentity.TenantId ?? "default";
        var operatorId = userIdentity.OperatorId ?? "anonymous";
        var ownerSubject = userIdentity.OwnerSubject ?? $"{tenantId}:{operatorId}";

        logger.LogInformation("开始雇佣流程: TemplateId={TemplateId}, TenantId={TenantId}", templateId, tenantId);

        var template = await templateDataProvider.GetByIdAsync(templateId, cancellationToken);
        if (template is null)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(404, $"模板 {templateId} 不存在");
        }

        var hireId = $"hire-{Guid.NewGuid():N}";
        var sessionId = $"session-{Guid.NewGuid():N}";

        // 创建沙箱
        var sandboxResult = await sandboxService.CreateAsync(new SandboxCreateRequestDto
        {
            ScopeType = "Hire",
            ScopeKey = hireId,
            SandboxRole = "candidate-conversation",
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            ProvisioningMode = "managed",
            UseCase = useCase ?? "hiring",
            TemplateId = templateId,
            IsInitialized = false
        }, cancellationToken);

        if (!sandboxResult.Success || sandboxResult.Data is null)
        {
            logger.LogError("创建沙箱失败: {Message}", sandboxResult.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, $"创建沙箱失败: {sandboxResult.Message}");
        }

        var sandboxId = sandboxResult.Data.SandboxId;

        // 创建雇佣会话记录
        dbContext.HiringSessions.Add(new HiringSessionEntity
        {
            SessionId = sessionId,
            HireId = hireId,
            TemplateId = templateId,
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        // 初始化阶段进度
        await hiringStageService.UpdateStageProgressAsync(hireId, "material", null, cancellationToken);

        logger.LogInformation("雇佣流程创建成功: HireId={HireId}, SandboxId={SandboxId}", hireId, sandboxId);

        return ApiResponse<HireTemplateResultDto>.SuccessResponse(new HireTemplateResultDto(
            HireId: hireId,
            SandboxId: sandboxId,
            Status: "created",
            NextAction: "start_conversation",
            SessionId: sessionId,
            GatewayEndpoint: sandboxResult.Data.GatewayEndpoint,
            TemplatePrimingRequired: true
        ));
    }

    public async Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.HiringSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (session is null)
        {
            return ApiResponse<HiringStatusDto>.ErrorResponse(404, $"找不到雇佣流程 {hireId}");
        }

        // 从沙箱表查询沙箱信息
        var sandbox = await dbContext.SandboxInstances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScopeType == "Hire" && x.ScopeKey == hireId, cancellationToken);

        var stageProgress = await hiringStageService.GetStageProgressAsync(hireId, cancellationToken);

        return ApiResponse<HiringStatusDto>.SuccessResponse(new HiringStatusDto(
            HireId: hireId,
            SandboxId: sandbox?.SandboxId ?? "",
            Status: sandbox?.State ?? "unknown",
            GatewayEndpoint: sandbox?.GatewayEndpoint,
            ErrorCode: null,
            ErrorMessage: null,
            CollectionPhase: "material",
            CurrentStage: stageProgress?.CurrentStage ?? "material"
        ));
    }

    public Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(
        string hireId,
        string? stage,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("GetStagePreviewAsync: 功能暂未实现");
        return Task.FromResult(ApiResponse<HiringStagePreviewDto>.ErrorResponse(501, "功能暂未实现"));
    }

    public Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(
        string hireId,
        HiringAuditDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("SubmitAuditDecisionAsync: 功能暂未实现");
        return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(501, "功能暂未实现"));
    }

    public Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ApiResponse<IReadOnlyList<HiringAuditLogDto>>.SuccessResponse(
            Array.Empty<HiringAuditLogDto>().ToList()));
    }

    public async Task<ApiResponse<HiringExternalSystemConfigDto>> GetExternalSystemConfigAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var config = await hiringStageService.GetExternalConfigAsync(hireId, cancellationToken);
        return ApiResponse<HiringExternalSystemConfigDto>.SuccessResponse(
            config ?? new HiringExternalSystemConfigDto());
    }

    public async Task<ApiResponse<HiringExternalSystemConfigDto>> SaveExternalSystemConfigAsync(
        string hireId,
        HiringExternalSystemConfigDto request,
        CancellationToken cancellationToken = default)
    {
        await hiringStageService.SaveExternalConfigAsync(hireId, request, cancellationToken);

        var stageProgress = await hiringStageService.GetStageProgressAsync(hireId, cancellationToken);
        if (stageProgress is not null && stageProgress.CurrentStage is "material" or "skill" or "external")
        {
            await hiringStageService.UpdateStageProgressAsync(hireId, "ready_for_packaging", null, cancellationToken);
        }

        return ApiResponse<HiringExternalSystemConfigDto>.SuccessResponse(request);
    }

    public Task<ApiResponse<HiringFinalizeResultDto>> ImportPackageAsync(
        string hireId,
        Stream packageStream,
        string fileName,
        IReadOnlyList<string>? linkedStoreSkillIds,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("ImportPackageAsync: 功能暂未实现");
        return Task.FromResult(ApiResponse<HiringFinalizeResultDto>.ErrorResponse(501, "功能暂未实现"));
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("BuildArtifactDownloadAsync: 功能暂未实现");
        return Task.FromResult(HiringArtifactDownloadResult.Error(501, "功能暂未实现"));
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(
        string hireId,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("BuildArtifactFileDownloadAsync: 功能暂未实现");
        return Task.FromResult(HiringArtifactDownloadResult.Error(501, "功能暂未实现"));
    }

    public Task<ApiResponse<HiringTemplatePackageUploadResultDto>> UploadTemplatePackageFromClientAsync(
        string hireId,
        Stream packageStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("UploadTemplatePackageFromClientAsync: 功能暂未实现");
        return Task.FromResult(ApiResponse<HiringTemplatePackageUploadResultDto>.ErrorResponse(501, "功能暂未实现"));
    }
}
