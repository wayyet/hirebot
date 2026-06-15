using System.IO.Compression;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Infrastructure.Identity;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Hiring.StoreSkills;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 雇佣服务（简化版：基于轻量表设计，移除 HiringRuntimeContext）。
/// </summary>
internal sealed class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    ITemplatePackageProvider templatePackageProvider,
    IDiscoveryRoleTemplatePackageProvider discoveryRoleTemplatePackageProvider,
    ISandboxService sandboxService,
    IHiringStageService hiringStageService,
    IEmployeeRuntimeService employeeRuntimeService,
    IHiringArtifactPackageService artifactPackageService,
    IStoreSkillPackageDownloader storeSkillPackageDownloader,
    IUserIdentity userIdentity,
    HireBotDbContext dbContext,
    IKingCrabHttpClient kingCrabHttpClient,
    IConfiguration configuration,
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

        // 检查是否存在现有的活跃沙箱（沙箱恢复逻辑）
        var existingInstance = await sandboxService.FindActiveByOwnerAndTemplateAsync(
            ownerSubject, templateId, "hiring", cancellationToken);

        if (existingInstance is not null)
        {
            logger.LogInformation("找到现有沙箱: SandboxId={SandboxId}, HireId={HireId}, State={State}, IsInitialized={IsInitialized}",
                existingInstance.SandboxId, existingInstance.ScopeKey, existingInstance.State, existingInstance.IsInitialized);

            // 如果沙箱已暂停，尝试恢复
            if (string.Equals(existingInstance.State, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("沙箱已暂停，尝试恢复: SandboxId={SandboxId}", existingInstance.SandboxId);
                await sandboxService.ResumeAsync(
                    new SandboxInstanceLookupRequestDto { SandboxId = existingInstance.SandboxId },
                    cancellationToken);
            }

            // 如果沙箱已初始化，直接复用
            if (existingInstance.IsInitialized)
            {
                // 刷新沙箱状态，验证沙箱在 OpenSandbox 中确实存活
                var refreshed = await sandboxService.RefreshAsync(
                    new SandboxInstanceLookupRequestDto { SandboxId = existingInstance.SandboxId },
                    cancellationToken);

                if (refreshed.Success && refreshed.Data is not null)
                {
                    existingInstance = refreshed.Data;
                }

                // 如果刷新后沙箱仍已初始化，直接复用
                if (existingInstance.IsInitialized)
                {
                    var existingHireId = existingInstance.ScopeKey;

                    // 从数据库查询会话 ID
                    var existingSessionId = await dbContext.HiringSessions
                        .AsNoTracking()
                        .Where(s => s.HireId == existingHireId && s.DeletedAtUtc == null)
                        .OrderByDescending(s => s.CreatedAtUtc)
                        .Select(s => s.SessionId)
                        .FirstOrDefaultAsync(cancellationToken);

                    var isRunning = string.Equals(existingInstance.State, "Running", StringComparison.OrdinalIgnoreCase);

                    logger.LogInformation("复用现有沙箱: HireId={HireId}, SandboxId={SandboxId}, SessionId={SessionId}, Status={Status}",
                        existingHireId, existingInstance.SandboxId, existingSessionId, isRunning ? "READY" : existingInstance.State);

                    return ApiResponse<HireTemplateResultDto>.SuccessResponse(
                        new HireTemplateResultDto(
                            HireId: existingHireId,
                            SandboxId: existingInstance.SandboxId,
                            Status: isRunning ? "READY" : existingInstance.State,
                            NextAction: "continue_conversation",
                            SessionId: existingSessionId,
                            GatewayEndpoint: isRunning ? existingInstance.GatewayEndpoint : null,
                            TemplatePrimingRequired: false),
                        "已复用现有沙箱");
                }
                else
                {
                    // 沙箱被外部删除后重建，IsInitialized 被重置为 false
                    logger.LogWarning("现有沙箱已被外部删除并重建，需要重新初始化: SandboxId={SandboxId}, HireId={HireId}",
                        existingInstance.SandboxId, existingInstance.ScopeKey);
                    // 继续使用现有的 hireId 和 sandboxId，重新初始化
                }
            }

            // 沙箱存在但未初始化，清理后重新创建
            if (!existingInstance.IsInitialized)
            {
                logger.LogInformation("现有沙箱未初始化，清理后重新创建: SandboxId={SandboxId}, HireId={HireId}",
                    existingInstance.SandboxId, existingInstance.ScopeKey);
                await sandboxService.DeleteAsync(
                    new SandboxInstanceLookupRequestDto { SandboxId = existingInstance.SandboxId },
                    cancellationToken);
            }
        }

        // 创建新的雇佣流程和沙箱
        var hireId = $"hire-{Guid.NewGuid():N}";
        var sessionId = $"session-{Guid.NewGuid():N}";

        logger.LogInformation("创建雇佣沙箱: HireId={HireId}", hireId);
        var sandboxResult = await sandboxService.CreateAsync(new SandboxCreateRequestDto
        {
            ScopeType = "Hire",
            ScopeKey = hireId,
            SandboxRole = "hiring",
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            ProvisioningMode = "managed",
            UseCase = useCase ?? "hiring",
            TemplateId = templateId,
            IsInitialized = false,
            Metadata = new Dictionary<string, string>
            {
                [SandboxMetaKeys.UserSubject] = ownerSubject,
                [SandboxMetaKeys.HireId] = hireId,
                [SandboxMetaKeys.TemplateId] = templateId,
                [SandboxMetaKeys.TenantId] = tenantId
            }
        }, cancellationToken);

        if (!sandboxResult.Success || sandboxResult.Data is null)
        {
            logger.LogError("创建沙箱失败: {Message}", sandboxResult.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, $"创建沙箱失败: {sandboxResult.Message}");
        }

        var sandboxId = sandboxResult.Data.SandboxId;

        try
        {
            // 等待沙箱启动到 Running 状态
            logger.LogInformation("等待沙箱启动: SandboxId={SandboxId}", sandboxId);
            var readyResult = await WaitForSandboxReadyAsync(sandboxResult.Data, cancellationToken);
            if (!readyResult.Success || readyResult.Data is null)
            {
                logger.LogError("沙箱启动失败: {Message}", readyResult.Message);
                await TryDeleteSandboxAsync(sandboxId, cancellationToken);
                return ApiResponse<HireTemplateResultDto>.ErrorResponse(readyResult.Code, $"沙箱启动失败: {readyResult.Message}");
            }

            var gatewayEndpoint = readyResult.Data.GatewayEndpoint;

            // 步骤 1: 上传雇佣对话教练模板 (employment-coach-conversation)
            logger.LogInformation("加载雇佣对话教练模板 (employment-coach-conversation)");
            var discoveryRolePackage = await discoveryRoleTemplatePackageProvider.LoadAsync(cancellationToken);

            logger.LogInformation("构建雇佣对话教练模板存档: PackageId={PackageId}, FileCount={FileCount}",
                discoveryRolePackage.PackageId, discoveryRolePackage.PackageFiles.Count);
            var discoveryArchiveBytes = TemplatePackageArchiveBuilder.BuildArchive(discoveryRolePackage);

            // 显式释放模板包引用，帮助 GC 尽快回收大对象
            discoveryRolePackage = null!;

            logger.LogInformation("上传雇佣对话教练模板到沙箱: SandboxId={SandboxId}, Size={Size}KB",
                sandboxId, discoveryArchiveBytes.Length / 1024);
            var discoveryUploadResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
                new DigitalEmployeeTemplateUploadRequestDto
                {
                    SandboxId = sandboxId,
                    OwnerSubject = ownerSubject,
                    ArchiveBytes = discoveryArchiveBytes,
                    FileName = "employment-coach-conversation.zip"
                },
                cancellationToken);

            // 释放存档字节数组引用
            discoveryArchiveBytes = null!;

            if (!discoveryUploadResult.Success || discoveryUploadResult.Data is null || !discoveryUploadResult.Data.Success)
            {
                var errorMsg = discoveryUploadResult.Data?.Error ?? discoveryUploadResult.Message;
                logger.LogError("上传雇佣对话教练模板失败: {Error}", errorMsg);

                // 上传失败，删除沙箱
                await TryDeleteSandboxAsync(sandboxId, cancellationToken);

                return ApiResponse<HireTemplateResultDto>.ErrorResponse(
                    discoveryUploadResult.Code > 0 ? discoveryUploadResult.Code : 500,
                    $"上传雇佣对话教练模板失败: {errorMsg}");
            }

            logger.LogInformation("雇佣对话教练模板上传成功: SkillsInstalled={SkillsInstalled}",
                discoveryUploadResult.Data.SkillsInstalled);

            // 步骤 2: 上传 MCP 配置（如果启用）
            // 注：目标模板包由前端通过其他接口上传到 workspace/uploads/
            var mcpUploadResult = await TryUploadMcpConfigAsync(sandboxId, ownerSubject, cancellationToken);
            if (!mcpUploadResult.Success)
            {
                logger.LogWarning("上传 MCP 配置失败（非致命错误）: {Error}", mcpUploadResult.Message);
                // MCP 配置上传失败不阻止流程继续
            }
            else if (mcpUploadResult.Data)
            {
                logger.LogInformation("MCP 配置已上传到沙箱: SandboxId={SandboxId}", sandboxId);
            }
            else
            {
                logger.LogDebug("MCP 配置未启用或未配置，跳过上传: SandboxId={SandboxId}", sandboxId);
            }

            // 标记沙箱已初始化（模板包已上传，可以使用）
            await SetSandboxInitializedAsync(sandboxId, cancellationToken);

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

            // 沙箱初始化完成后创建雇佣中状态的员工实例，让用户可以在员工列表看到正在雇佣的记录
            var capabilities = template.CoreAbilities ?? [];
            var createResponse = await employeeRuntimeService.CreateFromHireAsync(
                new CreateEmployeeFromHireRequestDto(
                    HireId: hireId,
                    TemplateId: templateId,
                    TemplateName: template.Name,
                    Description: template.Description,
                    OwnerSubject: ownerSubject,
                    TenantId: tenantId,
                    OperatorId: operatorId,
                    Capabilities: capabilities),
                cancellationToken);

            if (createResponse.Success && createResponse.Data is not null)
            {
                logger.LogInformation(
                    "已创建hiring状态的员工实例: HireId={HireId}, EmployeeId={EmployeeId}, Status=hiring",
                    hireId,
                    createResponse.Data.EmployeeId);
            }
            else
            {
                logger.LogWarning(
                    "创建hiring实例失败（非致命错误）: HireId={HireId}, Message={Message}",
                    hireId,
                    createResponse.Message);
            }

            // 初始化阶段进度（模板已上传，进入素材收集阶段）
            await hiringStageService.UpdateStageProgressAsync(hireId, "material", null, cancellationToken);

            logger.LogInformation("雇佣流程初始化成功: HireId={HireId}, SandboxId={SandboxId}", hireId, sandboxId);

            return ApiResponse<HireTemplateResultDto>.SuccessResponse(new HireTemplateResultDto(
                HireId: hireId,
                SandboxId: sandboxId,
                Status: "READY",
                NextAction: "start_conversation",
                SessionId: sessionId,
                GatewayEndpoint: gatewayEndpoint,
                TemplatePrimingRequired: false
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "雇佣流程初始化异常: HireId={HireId}, SandboxId={SandboxId}", hireId, sandboxId);

            // 异常发生，尝试删除沙箱
            await TryDeleteSandboxAsync(sandboxId, cancellationToken);

            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, $"雇佣流程初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 等待沙箱启动到 Running 状态（最多等待 3 分钟）。
    /// </summary>
    private async Task<ApiResponse<SandboxInstanceDto>> WaitForSandboxReadyAsync(
        SandboxInstanceDto instance,
        CancellationToken cancellationToken)
    {
        // 如果已经是 Running 状态，直接返回
        if (string.Equals(instance.State, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(instance.GatewayEndpoint))
        {
            logger.LogInformation("沙箱已就绪: SandboxId={SandboxId}", instance.SandboxId);
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(instance);
        }

        // 轮询等待，最多 36 次，每次间隔 5 秒（总计 3 分钟）
        for (var attempt = 1; attempt <= 36; attempt++)
        {
            logger.LogDebug("等待沙箱启动，第 {Attempt}/36 次尝试: SandboxId={SandboxId}, CurrentState={State}",
                attempt, instance.SandboxId, instance.State);

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            var refreshResult = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = instance.SandboxId
                },
                cancellationToken);

            if (!refreshResult.Success || refreshResult.Data is null)
            {
                logger.LogWarning("刷新沙箱状态失败: {Message}", refreshResult.Message);
                return ApiResponse<SandboxInstanceDto>.ErrorResponse(refreshResult.Code, refreshResult.Message);
            }

            if (string.Equals(refreshResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint))
            {
                logger.LogInformation("沙箱已启动: SandboxId={SandboxId}, 耗时 {Seconds} 秒",
                    instance.SandboxId, attempt * 5);
                return ApiResponse<SandboxInstanceDto>.SuccessResponse(refreshResult.Data);
            }

            // 如果状态是 Failed 或其他终止状态，提前退出
            if (string.Equals(refreshResult.Data.State, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("沙箱启动失败: SandboxId={SandboxId}, State={State}",
                    instance.SandboxId, refreshResult.Data.State);
                return ApiResponse<SandboxInstanceDto>.ErrorResponse(500, $"沙箱启动失败，状态: {refreshResult.Data.State}");
            }
        }

        logger.LogError("沙箱启动超时: SandboxId={SandboxId}, 已等待 3 分钟", instance.SandboxId);
        return ApiResponse<SandboxInstanceDto>.ErrorResponse(504, "沙箱启动超时（已等待 3 分钟）");
    }

    /// <summary>
    /// 加载模板包（每次从提供者加载，不缓存，用完即释放）。
    /// </summary>
    private Task<TemplatePackageDefinition> LoadTemplatePackageAsync(
        string templateId,
        CancellationToken cancellationToken)
    {
        // 直接从提供者加载，不走缓存，确保大对象能及时回收
        return templatePackageProvider.LoadAsync(templateId, cancellationToken);
    }

    /// <summary>
    /// 尝试删除沙箱（用于错误回滚）。
    /// </summary>
    private async Task TryDeleteSandboxAsync(string sandboxId, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogWarning("尝试删除沙箱: SandboxId={SandboxId}", sandboxId);
            await sandboxService.DeleteAsync(new SandboxInstanceLookupRequestDto
            {
                SandboxId = sandboxId
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "删除沙箱失败: SandboxId={SandboxId}", sandboxId);
        }
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

        // 将沙箱物理状态映射为雇佣流程逻辑状态（前端期望的格式）
        var hiringStatus = MapSandboxStateToHiringStatus(sandbox);

        return ApiResponse<HiringStatusDto>.SuccessResponse(new HiringStatusDto(
            HireId: hireId,
            SandboxId: sandbox?.SandboxId ?? "",
            Status: hiringStatus,
            GatewayEndpoint: sandbox?.GatewayEndpoint,
            ErrorCode: null,
            ErrorMessage: null,
            CollectionPhase: "material",
            CurrentStage: stageProgress?.CurrentStage ?? "material"
        ));
    }

    /// <summary>
    /// 将沙箱物理状态映射为雇佣流程逻辑状态。
    /// </summary>
    private static string MapSandboxStateToHiringStatus(SandboxInstanceEntity? sandbox)
    {
        if (sandbox is null)
        {
            return "PENDING";
        }

        // Running + 有网关端点 = 雇佣流程就绪
        if (string.Equals(sandbox.State, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(sandbox.GatewayEndpoint))
        {
            return "READY";
        }

        // Failed = 雇佣流程失败
        if (string.Equals(sandbox.State, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return "FAILED";
        }

        // 其他过渡状态（Allocated, Paused, Stopped 等）= 等待中
        return "PENDING";
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

    public async Task<ApiResponse<HiringSkillLinkConfigDto>> GetSkillLinkConfigAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var config = await hiringStageService.GetSkillLinkConfigAsync(hireId, cancellationToken);
        return ApiResponse<HiringSkillLinkConfigDto>.SuccessResponse(
            config ?? new HiringSkillLinkConfigDto());
    }

    public async Task<ApiResponse<HiringSkillLinkConfigDto>> SaveSkillLinkConfigAsync(
        string hireId,
        HiringSkillLinkConfigDto request,
        CancellationToken cancellationToken = default)
    {
        await hiringStageService.SaveSkillLinkConfigAsync(hireId, request, cancellationToken);
        var saved = await hiringStageService.GetSkillLinkConfigAsync(hireId, cancellationToken);
        return ApiResponse<HiringSkillLinkConfigDto>.SuccessResponse(
            saved ?? new HiringSkillLinkConfigDto());
    }

    public async Task<ApiResponse<HiringConversationSyncResultDto>> SyncConversationTurnAsync(
        string hireId,
        HiringConversationSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await PersistWorkspaceRootFromMaterialsAsync(hireId, request.Materials, cancellationToken);

        // 解析 AI 回复中的结构化数据标签（如 <data key="goal">...</data>）
        var extractedData = ParseStructuredDataTags(request.AssistantReply);

        if (extractedData.Count > 0)
        {
            logger.LogInformation("同步对话轮次，提取到 {Count} 个结构化数据字段: HireId={HireId}, Keys={Keys}",
                extractedData.Count, hireId, string.Join(", ", extractedData.Keys));

            // 批量保存到数据库
            await hiringStageService.SaveStructuredDataAsync(hireId, extractedData, cancellationToken);
        }
        else
        {
            logger.LogDebug("同步对话轮次，未提取到结构化数据: HireId={HireId}", hireId);
        }

        return ApiResponse<HiringConversationSyncResultDto>.SuccessResponse(
            new HiringConversationSyncResultDto(
                extractedData.Count,
                extractedData.Keys.ToList()));
    }

    private async Task PersistWorkspaceRootFromMaterialsAsync(
        string hireId,
        IReadOnlyList<HiringConversationMaterialDto>? materials,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = TryExtractWorkspaceRoot(materials);
        if (workspaceRoot is null)
        {
            return;
        }

        var sandbox = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.ScopeType == "Hire" && item.ScopeKey == hireId, cancellationToken);
        if (sandbox is null)
        {
            logger.LogWarning(
                "Workspace root was provided by conversation materials but sandbox was not found. HireId={HireId} WorkspaceRoot={WorkspaceRoot}",
                hireId,
                workspaceRoot);
            return;
        }

        sandbox.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (sandbox.Metadata.TryGetValue(SandboxMetaKeys.HiringWorkspaceRoot, out var existing) &&
            string.Equals(existing, workspaceRoot, StringComparison.Ordinal))
        {
            return;
        }

        sandbox.Metadata[SandboxMetaKeys.HiringWorkspaceRoot] = workspaceRoot;
        sandbox.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Persisted hiring workspace root from conversation materials. HireId={HireId} WorkspaceRoot={WorkspaceRoot}",
            hireId,
            workspaceRoot);
    }

    private static string? TryExtractWorkspaceRoot(IReadOnlyList<HiringConversationMaterialDto>? materials)
    {
        if (materials is null || materials.Count == 0)
        {
            return null;
        }

        foreach (var material in materials)
        {
            if (material.Metadata is null ||
                !material.Metadata.TryGetValue("workspaceDir", out var workspaceDir))
            {
                continue;
            }

            var normalized = NormalizeWorkspaceRoot(workspaceDir);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? NormalizeWorkspaceRoot(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        var trimmed = workspaceRoot.Trim().TrimEnd('/');
        return trimmed.StartsWith("/workspace/", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    public async Task<ApiResponse<Dictionary<string, string>>> GetStructuredDataAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var data = await hiringStageService.GetStructuredDataAsync(hireId, cancellationToken);

        // 转换为非空字典（过滤掉 null 值）
        var result = data
            .Where(kvp => kvp.Value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!, StringComparer.OrdinalIgnoreCase);

        return ApiResponse<Dictionary<string, string>>.SuccessResponse(result);
    }

    /// <summary>
    /// 解析 AI 回复中的结构化数据标签（支持单行和多行格式）。
    /// 示例：&lt;data key="goal"&gt;提升销售转化率&lt;/data&gt;
    /// </summary>
    private static Dictionary<string, string?> ParseStructuredDataTags(string assistantReply)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(assistantReply))
        {
            return result;
        }

        // 正则匹配 <data key="xxx">...</data>（支持多行内容）
        var regex = new System.Text.RegularExpressions.Regex(
            @"<data\s+key\s*=\s*[""']([^""']+)[""']\s*>(.*?)</data>",
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var matches = regex.Matches(assistantReply);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Success && match.Groups.Count >= 3)
            {
                var key = match.Groups[1].Value.Trim();
                var value = match.Groups[2].Value.Trim();

                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    result[key] = value;
                }
            }
        }

        return result;
    }

    private static readonly JsonSerializerOptions RuntimeStateSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ApiResponse<bool>> SaveRuntimeStateByStageAsync(
        string hireId,
        string stage,
        SaveRuntimeStateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedRuntimeStateStage(stage))
        {
            return ApiResponse<bool>.ErrorResponse(400, $"Unsupported runtime state stage: {stage}");
        }

        var progress = await dbContext.HiringStageProgresses
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (progress is null)
        {
            logger.LogWarning("Runtime state stage save failed because hiring progress was not found. HireId={HireId}, Stage={Stage}", hireId, stage);
            return ApiResponse<bool>.ErrorResponse(404, "Hiring progress not found.");
        }

        var stageOverrides = DeserializeRuntimeStateDictionary(progress.StageOverridesJson);
        var downstreamRuns = DeserializeDownstreamRuns(progress.DownstreamRunsJson);

        ApplyStageOverridePatch(stageOverrides, stage, request.StageOverrides);
        ApplyDownstreamRunPatch(downstreamRuns, stage, request.DownstreamRuns);

        progress.StageOverridesJson = SerializeOrNull(stageOverrides);
        progress.DownstreamRunsJson = SerializeOrNull(downstreamRuns);

        if (string.Equals(stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase))
        {
            progress.UploadedFilesJson = SerializeListOrNull(request.UploadedFiles);
        }

        if (string.Equals(stage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase))
        {
            progress.PackageStructureJson = SerializePackageStructureOrNull(request.PackageStructure);
        }

        progress.UpdatedAtUtc = DateTimeOffset.UtcNow;
        progress.UpdatedBy = userIdentity.OperatorId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Runtime state stage saved. HireId={HireId}, Stage={Stage}", hireId, stage);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<RuntimeStateDto>> GetRuntimeStateByStageAsync(
        string hireId,
        string stage,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupportedRuntimeStateStage(stage))
        {
            return ApiResponse<RuntimeStateDto>.ErrorResponse(400, $"Unsupported runtime state stage: {stage}");
        }

        var progress = await dbContext.HiringStageProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (progress is null)
        {
            return ApiResponse<RuntimeStateDto>.SuccessResponse(new RuntimeStateDto());
        }

        var stageOverrides = FilterStageOverridesByStage(DeserializeRuntimeStateDictionary(progress.StageOverridesJson), stage);
        var downstreamRuns = FilterDownstreamRunsByStage(DeserializeDownstreamRuns(progress.DownstreamRunsJson), stage);

        IReadOnlyList<PersistedChatFileDto>? uploadedFiles = null;
        if (string.Equals(stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase))
        {
            uploadedFiles = DeserializeUploadedFiles(progress.UploadedFilesJson);
        }

        PersistedPackageStructureDto? packageStructure = null;
        if (string.Equals(stage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase))
        {
            packageStructure = DeserializePackageStructure(progress.PackageStructureJson);
        }

        return ApiResponse<RuntimeStateDto>.SuccessResponse(new RuntimeStateDto
        {
            StageOverrides = stageOverrides.Count > 0 ? stageOverrides : null,
            DownstreamRuns = downstreamRuns.Count > 0 ? downstreamRuns : null,
            UploadedFiles = uploadedFiles,
            PackageStructure = packageStructure,
        });
    }

    private static bool IsSupportedRuntimeStateStage(string stage)
    {
        return string.Equals(stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> DeserializeRuntimeStateDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json, RuntimeStateSerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, DownstreamRunInfo> DeserializeDownstreamRuns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, DownstreamRunInfo>>(json, RuntimeStateSerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<PersistedChatFileDto>? DeserializeUploadedFiles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<PersistedChatFileDto>>(json, RuntimeStateSerializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "反序列化上传文件列表失败");
            return null;
        }
    }

    private PersistedPackageStructureDto? DeserializePackageStructure(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PersistedPackageStructureDto>(json, RuntimeStateSerializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "反序列化产物包结构失败");
            return null;
        }
    }

    private static IReadOnlyCollection<string> GetDownstreamRunKeys(string stage)
    {
        if (string.Equals(stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase))
        {
            return ["ontology-extraction", "ontology-projection"];
        }

        if (string.Equals(stage, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase))
        {
            return ["skill-generation"];
        }

        if (string.Equals(stage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase))
        {
            return ["packaging-test-cases"];
        }

        return [];
    }

    private static Dictionary<string, object> FilterStageOverridesByStage(
        Dictionary<string, object> stageOverrides,
        string stage)
    {
        if (stageOverrides.TryGetValue(stage, out var value))
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [stage] = value,
            };
        }

        return [];
    }

    private static Dictionary<string, DownstreamRunInfo> FilterDownstreamRunsByStage(
        Dictionary<string, DownstreamRunInfo> downstreamRuns,
        string stage)
    {
        var keys = GetDownstreamRunKeys(stage);
        return downstreamRuns
            .Where(entry => keys.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyStageOverridePatch(
        Dictionary<string, object> current,
        string stage,
        IReadOnlyDictionary<string, object>? incoming)
    {
        current.Remove(stage);

        if (incoming is null)
        {
            return;
        }

        if (incoming.TryGetValue(stage, out var value))
        {
            current[stage] = value;
        }
    }

    private static void ApplyDownstreamRunPatch(
        Dictionary<string, DownstreamRunInfo> current,
        string stage,
        IReadOnlyDictionary<string, DownstreamRunInfo>? incoming)
    {
        var allowedKeys = GetDownstreamRunKeys(stage);
        foreach (var key in allowedKeys)
        {
            current.Remove(key);
        }

        if (incoming is null)
        {
            return;
        }

        foreach (var entry in incoming)
        {
            if (allowedKeys.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                current[entry.Key] = entry.Value;
            }
        }
    }

    private static string? SerializeOrNull<TValue>(Dictionary<string, TValue> value)
    {
        return value.Count == 0
            ? null
            : JsonSerializer.Serialize(value, RuntimeStateSerializerOptions);
    }

    private static string? SerializeListOrNull<TValue>(IReadOnlyList<TValue>? value)
    {
        return value is { Count: > 0 }
            ? JsonSerializer.Serialize(value, RuntimeStateSerializerOptions)
            : null;
    }

    private static string? SerializePackageStructureOrNull(PersistedPackageStructureDto? value)
    {
        return value is not null && !string.IsNullOrWhiteSpace(value.FileName)
            ? JsonSerializer.Serialize(value, RuntimeStateSerializerOptions)
            : null;
    }

    public async Task<ApiResponse<HiringFinalizeResultDto>> ImportPackageAsync(
        string hireId,
        Stream packageStream,
        string fileName,
        IReadOnlyList<string>? linkedStoreSkillIds,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("开始导入候选包: HireId={HireId}, FileName={FileName}, LinkedSkills={SkillCount}",
            hireId, fileName, linkedStoreSkillIds?.Count ?? 0);

        // 验证雇佣会话是否存在
        var session = await dbContext.HiringSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (session is null)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(404, $"找不到雇佣流程 {hireId}");
        }

        // 查询关联的沙箱信息
        var sandbox = await dbContext.SandboxInstances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScopeType == "Hire" && x.ScopeKey == hireId, cancellationToken);

        if (sandbox is null)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(404, $"找不到雇佣流程 {hireId} 关联的沙箱");
        }

        try
        {
            // 将包文件内容读取到内存（用于持久化和后续处理）
            byte[] packageBytes;
            using (var ms = new MemoryStream())
            {
                await packageStream.CopyToAsync(ms, cancellationToken);
                packageBytes = ms.ToArray();
            }

            logger.LogInformation("候选包已读取到内存: Size={Size}KB", packageBytes.Length / 1024);

            // 解压 ZIP，提取文件条目后持久化到文件系统和数据库
            var extractedFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var (path, content) in ExtractZipEntries(packageBytes))
                {
                    extractedFiles[path] = content;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "候选包 ZIP 格式无效，无法解压: HireId={HireId}, FileName={FileName}", hireId, fileName);
                return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(400, $"候选包 ZIP 格式无效: {ex.Message}");
            }

            // 每次导入生成唯一包版本 ID，确保多次导入的包不互相覆盖
            var effectiveSkillLinkConfig = await ResolveEffectiveSkillLinkConfigAsync(
                hireId,
                linkedStoreSkillIds,
                cancellationToken);
            var linkedStoreSkillRequests = BuildStoreSkillDownloadRequests(effectiveSkillLinkConfig);
            if (linkedStoreSkillRequests.Count > 0)
            {
                var downloadedSkillFiles = await storeSkillPackageDownloader.DownloadSkillsAsync(
                    linkedStoreSkillRequests,
                    cancellationToken);
                foreach (var (path, content) in downloadedSkillFiles)
                {
                    extractedFiles[path] = content;
                }
            }

            extractedFiles["skills/linked-store-skills.index.json"] = BuildSkillLinkIndexFile(effectiveSkillLinkConfig);

            var packageId = Guid.NewGuid().ToString("N");
            var packageFileName = await BuildFinalPackageFileNameAsync(
                hireId,
                session.TemplateId,
                cancellationToken);

            await artifactPackageService.PersistFinalPackageAsync(
                new HiringArtifactPackagePersistRequestDto(
                    HireId: hireId,
                    SessionId: session.SessionId,
                    FileName: packageFileName,
                    Files: extractedFiles,
                    PackageId: packageId),
                cancellationToken);

            logger.LogInformation("候选包已持久化: HireId={HireId}, SessionId={SessionId}, FileCount={FileCount}, PackageId={PackageId}",
                hireId, session.SessionId, extractedFiles.Count, packageId);

            // 通过 HireId 精确查找关联的数字员工实例，避免同一模板多个并发流程时找错实例
            var hiringInstance = await dbContext.Instances
                .Where(i => i.HireId == hireId &&
                            (i.Status == EmployeeStatus.Hiring || i.Status == EmployeeStatus.InterningAi))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            string? resolvedEmployeeId = null;
            var sessionTenantId = string.IsNullOrWhiteSpace(session.TenantId) ? "default" : session.TenantId.Trim();
            var reusableHiringInstance = hiringInstance is null
                ? await dbContext.Instances
                    .AsNoTracking()
                    .Where(i => i.TenantId == sessionTenantId &&
                                i.OwnerUserId == session.OwnerSubject &&
                                i.BasedOnTemplateId == session.TemplateId &&
                                i.InstanceType == "department" &&
                                i.Status == EmployeeStatus.Hiring)
                    .OrderByDescending(i => i.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            if (hiringInstance is not null)
            {
                resolvedEmployeeId = hiringInstance.InstanceId;

                // 仅在 Hiring 状态时才做状态迁移；InterningAi 表示已导入过，跳过重复更新
                if (hiringInstance.Status == EmployeeStatus.Hiring)
                {
                    var updateResult = await employeeRuntimeService.UpdateLifecycleAsync(
                        resolvedEmployeeId,
                        new UpdateEmployeeLifecycleRequestDto
                        {
                            Status = EmployeeStatus.InterningAi,
                            LifecycleStatus = "待AI评估",
                            StageSummary = "候选包已导入，可发起 AI 评估",
                            PrimarySignal = "待操作：发起 AI 评估",
                            SignalLevel = "ok"
                        },
                        cancellationToken);

                    if (updateResult.Success)
                    {
                        logger.LogInformation(
                            "员工状态已更新: HireId={HireId}, EmployeeId={EmployeeId}, Status=hiring→interning_ai",
                            hireId,
                            resolvedEmployeeId);
                    }
                    else
                    {
                        logger.LogWarning(
                            "员工状态更新失败（非致命错误）: HireId={HireId}, EmployeeId={EmployeeId}, Error={Error}",
                            hireId,
                            resolvedEmployeeId,
                            updateResult.Message);
                    }
                }
                else
                {
                    logger.LogInformation(
                        "员工实例已处于 {Status} 状态，跳过重复状态迁移: HireId={HireId}, EmployeeId={EmployeeId}",
                        hiringInstance.Status,
                        hireId,
                        resolvedEmployeeId);
                }
            }
            else if (reusableHiringInstance is not null)
            {
                resolvedEmployeeId = reusableHiringInstance.InstanceId;
                logger.LogWarning(
                    "Import package found reusable hiring instance with stale HireId. CurrentHireId={HireId}, OldHireId={OldHireId}, EmployeeId={EmployeeId}",
                    hireId,
                    reusableHiringInstance.HireId,
                    resolvedEmployeeId);

                var updateResult = await employeeRuntimeService.UpdateLifecycleAsync(
                    resolvedEmployeeId,
                    new UpdateEmployeeLifecycleRequestDto
                    {
                        Status = EmployeeStatus.InterningAi,
                        LifecycleStatus = "待AI评估",
                        StageSummary = "候选包已导入，可发起 AI 评估",
                        PrimarySignal = "待操作：发起 AI 评估",
                        SignalLevel = "ok"
                    },
                    cancellationToken);

                if (updateResult.Success)
                {
                    await dbContext.Instances
                        .Where(i => i.InstanceId == resolvedEmployeeId)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(i => i.HireId, hireId)
                                .SetProperty(i => i.FinalPackageId, packageId),
                            cancellationToken);

                    logger.LogInformation(
                        "Reusable hiring instance rebound and moved to interning_ai. HireId={HireId}, EmployeeId={EmployeeId}",
                        hireId,
                        resolvedEmployeeId);
                }
                else
                {
                    logger.LogWarning(
                        "Reusable hiring instance lifecycle update failed. HireId={HireId}, EmployeeId={EmployeeId}, Error={Error}",
                        hireId,
                        resolvedEmployeeId,
                        updateResult.Message);
                }
            }
            else
            {
                // 员工实例不存在（可能已被删除），重新创建并直接置为待AI评估状态
                logger.LogWarning(
                    "未找到 Hiring/InterningAi 状态的员工实例，将重新创建: HireId={HireId}, TemplateId={TemplateId}",
                    hireId,
                    session.TemplateId);

                var templateForRecreate = await templateDataProvider.GetByIdAsync(session.TemplateId, cancellationToken);
                var recreateCapabilities = templateForRecreate?.CoreAbilities ?? [];

                var recreateResponse = await employeeRuntimeService.CreateFromHireAsync(
                    new CreateEmployeeFromHireRequestDto(
                        HireId: hireId,
                        TemplateId: session.TemplateId,
                        TemplateName: templateForRecreate?.Name ?? session.TemplateId,
                        Description: templateForRecreate?.Description,
                        OwnerSubject: session.OwnerSubject,
                        TenantId: session.TenantId ?? "default",
                        OperatorId: session.OperatorId,
                        Capabilities: recreateCapabilities),
                    cancellationToken);

                if (recreateResponse.Success && recreateResponse.Data is not null)
                {
                    resolvedEmployeeId = recreateResponse.Data.EmployeeId;

                    var updateResult = await employeeRuntimeService.UpdateLifecycleAsync(
                        resolvedEmployeeId,
                        new UpdateEmployeeLifecycleRequestDto
                        {
                            Status = EmployeeStatus.InterningAi,
                            LifecycleStatus = "待AI评估",
                            StageSummary = "候选包已导入，可发起 AI 评估",
                            PrimarySignal = "待操作：发起 AI 评估",
                            SignalLevel = "ok"
                        },
                        cancellationToken);

                    if (updateResult.Success)
                    {
                        logger.LogInformation(
                            "已重新创建员工实例并更新为待AI评估: HireId={HireId}, EmployeeId={EmployeeId}",
                            hireId,
                            resolvedEmployeeId);
                    }
                    else
                    {
                        logger.LogWarning(
                            "重新创建的员工实例状态更新失败（非致命错误）: HireId={HireId}, EmployeeId={EmployeeId}, Error={Error}",
                            hireId,
                            resolvedEmployeeId,
                            updateResult.Message);
                    }
                }
                else
                {
                    logger.LogWarning(
                        "重新创建员工实例失败（非致命错误）: HireId={HireId}, Message={Message}",
                        hireId,
                        recreateResponse.Message);
                }
            }

            // 保存导入元数据到结构化数据
            var importMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["imported_package_file"] = fileName,
                ["imported_package_size"] = packageBytes.Length.ToString(),
                ["imported_at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["import_method"] = "direct_upload",
                ["employee_status"] = EmployeeStatus.InterningAi // 标记为AI评估阶段
            };

            // 写入员工实例 ID 反向索引，评估服务通过 employeeId 可反查到本 hireId 的 artifact
            if (!string.IsNullOrWhiteSpace(resolvedEmployeeId))
            {
                importMetadata["linked_employee_id"] = resolvedEmployeeId;
            }

            if (effectiveSkillLinkConfig.LinkedSkills.Count > 0)
            {
                importMetadata["linked_store_skills"] = string.Join(
                    ",",
                    effectiveSkillLinkConfig.LinkedSkills.Select(static item => item.SkillId));
            }

            // 合并现有结构化数据
            var existingData = await hiringStageService.GetStructuredDataAsync(hireId, cancellationToken);
            var mergedData = new Dictionary<string, string?>(existingData, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in importMetadata)
            {
                mergedData[key] = value;
            }

            await hiringStageService.SaveStructuredDataAsync(hireId, mergedData, cancellationToken);

            // 将当前版本包 ID 写入实例表，供评估服务按版本精确查找
            if (!string.IsNullOrWhiteSpace(resolvedEmployeeId))
            {
                await dbContext.Instances
                    .Where(i => i.InstanceId == resolvedEmployeeId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(i => i.FinalPackageId, packageId),
                        cancellationToken);
            }

            logger.LogInformation("候选包导入成功: HireId={HireId}, 员工状态已更新为interning_ai", hireId);

            // 返回成功结果
            return ApiResponse<HiringFinalizeResultDto>.SuccessResponse(
                new HiringFinalizeResultDto(
                    HireId: hireId,
                    CurrentStage: HiringCollectionStage.ReadyForPackaging,
                    CollectionPhase: "finalized",
                    GeneratedFiles: Array.Empty<string>(),
                    DownloadUrl: $"/api/v1/hirings/{hireId}/artifacts/download",
                    EmployeeId: resolvedEmployeeId,
                    PackageFileName: packageFileName),
                "候选包导入成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "导入候选包失败: HireId={HireId}, FileName={FileName}", hireId, fileName);
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(500, $"导入候选包失败: {ex.Message}");
        }
    }

    public async Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var result = await artifactPackageService.BuildFinalPackageDownloadAsync(hireId, cancellationToken);
        if (!result.Found || result.Content is null || string.IsNullOrWhiteSpace(result.ContentType))
        {
            return result;
        }

        var packageFileName = await BuildFinalPackageFileNameAsync(
            hireId,
            templateId: null,
            cancellationToken);

        return HiringArtifactDownloadResult.Success(
            packageFileName,
            result.ContentType,
            result.Content);
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(
        string hireId,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        return artifactPackageService.BuildFinalPackageFileDownloadAsync(hireId, artifactName, cancellationToken);
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

    private async Task<string> BuildFinalPackageFileNameAsync(
        string hireId,
        string? templateId,
        CancellationToken cancellationToken)
    {
        var resolvedTemplateId = string.IsNullOrWhiteSpace(templateId)
            ? await dbContext.HiringSessions
                .AsNoTracking()
                .Where(session => session.HireId == hireId && session.DeletedAtUtc == null)
                .OrderByDescending(session => session.CreatedAtUtc)
                .Select(session => session.TemplateId)
                .FirstOrDefaultAsync(cancellationToken)
            : templateId.Trim();

        var templateName = resolvedTemplateId;
        if (!string.IsNullOrWhiteSpace(resolvedTemplateId))
        {
            try
            {
                var template = await templateDataProvider.GetByIdAsync(resolvedTemplateId, cancellationToken);
                templateName = template?.Name ?? resolvedTemplateId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "无法加载模板名称，最终包下载名将回退到模板 ID。HireId={HireId}, TemplateId={TemplateId}",
                    hireId,
                    resolvedTemplateId);
            }
        }

        return HiringPackageFileNames.BuildFinalPackageFileName(
            templateName,
            hireId);
    }

    /// <summary>
    /// 标记沙箱已完全初始化（模板包已上传，配置已设置，可以使用）。
    /// </summary>
    private async Task<HiringSkillLinkConfigDto> ResolveEffectiveSkillLinkConfigAsync(
        string hireId,
        IReadOnlyList<string>? fallbackSkillIds,
        CancellationToken cancellationToken)
    {
        var persistedConfig = await hiringStageService.GetSkillLinkConfigAsync(hireId, cancellationToken);
        if (persistedConfig is { LinkedSkills.Count: > 0 })
        {
            return persistedConfig;
        }

        if (fallbackSkillIds is not { Count: > 0 })
        {
            return new HiringSkillLinkConfigDto();
        }

        var linkedSkills = fallbackSkillIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => new HiringLinkedSkillItemDto
            {
                SkillId = id.Trim(),
                BindingMode = "manual"
            })
            .GroupBy(static item => item.SkillId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        return new HiringSkillLinkConfigDto
        {
            SubmissionMode = linkedSkills.Length > 0
                ? HiringSkillLinkSubmissionModes.Configured
                : HiringSkillLinkSubmissionModes.Pending,
            LinkedSkills = linkedSkills
        };
    }

    private static IReadOnlyList<StoreSkillDownloadRequest> BuildStoreSkillDownloadRequests(HiringSkillLinkConfigDto config)
    {
        if (config.LinkedSkills.Count == 0)
        {
            return [];
        }

        return config.LinkedSkills
            .Where(static item => !string.IsNullOrWhiteSpace(item.SkillId))
            .Select(static item => new StoreSkillDownloadRequest(
                SkillId: item.SkillId.Trim(),
                VersionId: string.IsNullOrWhiteSpace(item.VersionId) ? null : item.VersionId.Trim(),
                PreferredSlug: string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim()))
            .ToArray();
    }

    private static byte[] BuildSkillLinkIndexFile(HiringSkillLinkConfigDto config)
    {
        var skills = config.LinkedSkills
            .Where(static item => !string.IsNullOrWhiteSpace(item.SkillId))
            .Select(item => new
            {
                skillId = item.SkillId.Trim(),
                name = item.Name?.Trim() ?? string.Empty,
                displayName = item.DisplayName?.Trim() ?? string.Empty,
                versionId = item.VersionId?.Trim() ?? string.Empty,
                currentVersion = item.CurrentVersion?.Trim() ?? string.Empty,
                bindingMode = string.IsNullOrWhiteSpace(item.BindingMode) ? "manual" : item.BindingMode.Trim(),
                path = BuildLinkedSkillPath(item)
            })
            .ToArray();

        var indexDocument = new
        {
            schemaVersion = "1.0.0",
            artifactType = "linked_store_skills_index",
            submissionMode = skills.Length > 0
                ? HiringSkillLinkSubmissionModes.Configured
                : HiringSkillLinkSubmissionModes.Pending,
            skills,
            summary = new
            {
                total = skills.Length
            }
        };

        return JsonSerializer.SerializeToUtf8Bytes(indexDocument, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
    }

    private static string BuildLinkedSkillPath(HiringLinkedSkillItemDto item)
    {
        var preferredName = item.Name?.Trim();
        if (string.IsNullOrWhiteSpace(preferredName))
        {
            preferredName = item.SkillId?.Trim();
        }

        return $"skills/{SanitizeSkillSlug(preferredName)}/";
    }

    private static string SanitizeSkillSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "skill";
        }

        var chars = raw.Select(ch =>
            char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'
                ? ch
                : '-').ToArray();
        var slug = new string(chars).Trim('-', '.');
        return string.IsNullOrWhiteSpace(slug) ? "skill" : slug;
    }

    private async Task SetSandboxInitializedAsync(string sandboxId, CancellationToken cancellationToken)
    {
        var instance = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.SandboxId == sandboxId, cancellationToken);

        if (instance is not null && !instance.IsInitialized)
        {
            instance.IsInitialized = true;
            instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("沙箱已标记为已初始化: SandboxId={SandboxId}", sandboxId);
        }
    }

    /// <summary>
    /// 尝试上传全局 MCP 配置到沙箱（从 appsettings.json 读取）。
    /// 此方法仅处理项目默认 MCP 配置，不涉及用户自定义配置。
    /// </summary>
    private async Task<ApiResponse<bool>> TryUploadMcpConfigAsync(
        string sandboxId,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. 从配置文件读取全局 MCP 配置（项目默认配置）
            var mcpConfig = ReadMcpConfig();

            // 2. 如果配置未启用或无有效服务器，跳过上传
            if (mcpConfig is null || !mcpConfig.Enabled || mcpConfig.Servers.Count == 0)
            {
                logger.LogDebug("MCP 配置未启用或未配置服务器，跳过上传: SandboxId={SandboxId}", sandboxId);
                return ApiResponse<bool>.SuccessResponse(false, "MCP 配置未启用");
            }

            // 3. 获取沙箱的 Gateway 端点
            var sandbox = await dbContext.SandboxInstances.AsNoTracking()
                .FirstOrDefaultAsync(x => x.SandboxId == sandboxId, cancellationToken);

            if (sandbox is null)
            {
                logger.LogWarning("未找到沙箱实例: SandboxId={SandboxId}", sandboxId);
                return ApiResponse<bool>.ErrorResponse(404, "未找到沙箱实例");
            }

            if (string.IsNullOrWhiteSpace(sandbox.GatewayEndpoint))
            {
                logger.LogWarning("沙箱 Gateway 端点尚未就绪: SandboxId={SandboxId}", sandboxId);
                return ApiResponse<bool>.ErrorResponse(409, "沙箱 Gateway 端点尚未就绪");
            }

            // 4. 通过 HTTP PUT 请求上传 MCP 配置到沙箱
            var uploadResult = await kingCrabHttpClient.SendForJsonAsync<SandboxMcpConfigResponse>(
                HttpMethod.Put,
                "/admin/workspace/mcp",
                mcpConfig,
                ownerSubject,
                cancellationToken,
                useHireBotApiPrefix: false,
                absoluteBaseUrl: sandbox.GatewayEndpoint);

            if (!uploadResult.Success || uploadResult.Data is null)
            {
                logger.LogWarning(
                    "MCP 配置上传失败（非致命错误）: SandboxId={SandboxId}, StatusCode={StatusCode}, Message={Message}",
                    sandboxId,
                    uploadResult.StatusCode,
                    uploadResult.Message);
                return ApiResponse<bool>.ErrorResponse(
                    uploadResult.StatusCode > 0 ? uploadResult.StatusCode : 502,
                    uploadResult.Message ?? "MCP 配置上传失败");
            }

            logger.LogInformation(
                "MCP 配置已上传到沙箱: SandboxId={SandboxId}, ServerCount={ServerCount}",
                sandboxId,
                mcpConfig.Servers.Count);

            return ApiResponse<bool>.SuccessResponse(true, "MCP 配置上传成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "上传全局 MCP 配置异常: SandboxId={SandboxId}", sandboxId);
            return ApiResponse<bool>.ErrorResponse(500, $"上传 MCP 配置异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 从配置文件读取全局 MCP 配置（项目默认配置）。
    /// 配置路径：OpenSandbox:McpConfig
    /// 仅当 Enabled=true 时返回配置对象，否则返回 null。
    /// </summary>
    private SandboxWorkspaceMcpConfig? ReadMcpConfig()
    {
        var config = configuration.GetSection("OpenSandbox:McpConfig").Get<SandboxWorkspaceMcpConfig>();
        return config?.Enabled == true ? config : null;
    }

    /// <summary>
    /// MCP 配置上传响应模型（与 OpenSandbox Gateway 的响应格式对齐）。
    /// </summary>
    private sealed record SandboxMcpConfigResponse(
        bool Success,
        string? Message = null);

    /// <summary>
    /// 从 ZIP 字节数组中提取所有文件条目，返回 路径→内容 字典。
    /// 目录条目（Name 为空）会被跳过。
    /// </summary>
    private static IReadOnlyDictionary<string, byte[]> ExtractZipEntries(byte[] zipBytes)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var stream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            // 跳过目录条目（Name 为空时表示目录）
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            files[entry.FullName] = buffer.ToArray();
        }

        return files;
    }
}
