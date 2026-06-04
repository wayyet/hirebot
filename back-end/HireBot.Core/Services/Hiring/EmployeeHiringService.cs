using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Infrastructure.Identity;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 雇佣服务（简化版：基于轻量表设计，移除 HiringRuntimeContext）。
/// </summary>
internal sealed class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    ITemplatePackageProvider templatePackageProvider,
    ISandboxService sandboxService,
    IHiringStageService hiringStageService,
    IUserIdentity userIdentity,
    HireBotDbContext dbContext,
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
        logger.LogInformation("创建雇佣沙箱: HireId={HireId}", hireId);
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

            // 加载并上传数字员工模板到沙箱
            logger.LogInformation("加载模板包: TemplateId={TemplateId}", templateId);
            var templatePackage = await LoadTemplatePackageAsync(templateId, cancellationToken);
            
            logger.LogInformation("构建模板包存档: PackageId={PackageId}, FileCount={FileCount}", 
                templatePackage.PackageId, templatePackage.PackageFiles.Count);
            var archiveBytes = TemplatePackageArchiveBuilder.BuildArchive(templatePackage);
            
            // 显式释放模板包引用，帮助 GC 尽快回收大对象
            templatePackage = null!;
            
            logger.LogInformation("上传模板包到沙箱: SandboxId={SandboxId}, Size={Size}KB", 
                sandboxId, archiveBytes.Length / 1024);
            var uploadResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
                new DigitalEmployeeTemplateUploadRequestDto
                {
                    SandboxId = sandboxId,
                    OwnerSubject = ownerSubject,
                    ArchiveBytes = archiveBytes,
                    FileName = $"{templateId}-hiring.zip"
                },
                cancellationToken);

            // 释放存档字节数组引用
            archiveBytes = null!;

            if (!uploadResult.Success || uploadResult.Data is null || !uploadResult.Data.Success)
            {
                var errorMsg = uploadResult.Data?.Error ?? uploadResult.Message;
                logger.LogError("上传模板包失败: {Error}", errorMsg);
                
                // 上传失败，删除沙箱
                await TryDeleteSandboxAsync(sandboxId, cancellationToken);
                
                return ApiResponse<HireTemplateResultDto>.ErrorResponse(
                    uploadResult.Code > 0 ? uploadResult.Code : 500,
                    $"上传模板包失败: {errorMsg}");
            }

            logger.LogInformation("模板包上传成功: SkillsInstalled={SkillsInstalled}", uploadResult.Data.SkillsInstalled);

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

    public async Task<ApiResponse<HiringConversationSyncResultDto>> SyncConversationTurnAsync(
        string hireId,
        HiringConversationSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
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
    private static Dictionary<string, string> ParseStructuredDataTags(string assistantReply)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
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

    public async Task<ApiResponse<bool>> SaveRuntimeStateAsync(
        string hireId,
        SaveRuntimeStateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var progress = await dbContext.HiringStageProgresses
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (progress is null)
        {
            logger.LogWarning("保存运行时状态失败，雇佣会话不存在: HireId={HireId}", hireId);
            return ApiResponse<bool>.ErrorResponse(404, "雇佣会话不存在");
        }

        // 保存阶段覆盖配置
        if (request.StageOverrides is not null)
        {
            progress.StageOverridesJson = System.Text.Json.JsonSerializer.Serialize(
                request.StageOverrides,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        // 保存下游运行记录
        if (request.DownstreamRuns is not null)
        {
            progress.DownstreamRunsJson = System.Text.Json.JsonSerializer.Serialize(
                request.DownstreamRuns,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        progress.UpdatedAtUtc = DateTimeOffset.UtcNow;
        progress.UpdatedBy = userIdentity.OperatorId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("保存运行时状态成功: HireId={HireId}, HasOverrides={HasOverrides}, HasRuns={HasRuns}",
            hireId, request.StageOverrides is not null, request.DownstreamRuns is not null);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<RuntimeStateDto>> GetRuntimeStateAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var progress = await dbContext.HiringStageProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (progress is null)
        {
            return ApiResponse<RuntimeStateDto>.SuccessResponse(new RuntimeStateDto());
        }

        // 反序列化阶段覆盖配置
        IReadOnlyDictionary<string, object>? stageOverrides = null;
        if (!string.IsNullOrWhiteSpace(progress.StageOverridesJson))
        {
            try
            {
                stageOverrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                    progress.StageOverridesJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "反序列化阶段覆盖配置失败: HireId={HireId}", hireId);
            }
        }

        // 反序列化下游运行记录
        IReadOnlyDictionary<string, DownstreamRunInfo>? downstreamRuns = null;
        if (!string.IsNullOrWhiteSpace(progress.DownstreamRunsJson))
        {
            try
            {
                downstreamRuns = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, DownstreamRunInfo>>(
                    progress.DownstreamRunsJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "反序列化下游运行记录失败: HireId={HireId}", hireId);
            }
        }

        return ApiResponse<RuntimeStateDto>.SuccessResponse(new RuntimeStateDto
        {
            StageOverrides = stageOverrides,
            DownstreamRuns = downstreamRuns
        });
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
