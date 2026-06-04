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
            ownerSubject, templateId, "candidate-conversation", cancellationToken);

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

        // 保存对话上传文件列表
        if (request.UploadedFiles is not null)
        {
            progress.UploadedFilesJson = System.Text.Json.JsonSerializer.Serialize(
                request.UploadedFiles,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        // 保存最新产物包结构
        if (request.PackageStructure is not null)
        {
            progress.PackageStructureJson = System.Text.Json.JsonSerializer.Serialize(
                request.PackageStructure,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        progress.UpdatedAtUtc = DateTimeOffset.UtcNow;
        progress.UpdatedBy = userIdentity.OperatorId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("保存运行时状态成功: HireId={HireId}, HasOverrides={HasOverrides}, HasRuns={HasRuns}, HasFiles={HasFiles}, HasPackage={HasPackage}",
            hireId, request.StageOverrides is not null, request.DownstreamRuns is not null,
            request.UploadedFiles is not null, request.PackageStructure is not null);

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

        // 反序列化对话上传文件列表
        IReadOnlyList<PersistedChatFileDto>? uploadedFiles = null;
        if (!string.IsNullOrWhiteSpace(progress.UploadedFilesJson))
        {
            try
            {
                uploadedFiles = System.Text.Json.JsonSerializer.Deserialize<List<PersistedChatFileDto>>(
                    progress.UploadedFilesJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "反序列化上传文件列表失败: HireId={HireId}", hireId);
            }
        }

        // 反序列化最新产物包结构
        PersistedPackageStructureDto? packageStructure = null;
        if (!string.IsNullOrWhiteSpace(progress.PackageStructureJson))
        {
            try
            {
                packageStructure = System.Text.Json.JsonSerializer.Deserialize<PersistedPackageStructureDto>(
                    progress.PackageStructureJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "反序列化产物包结构失败: HireId={HireId}", hireId);
            }
        }

        return ApiResponse<RuntimeStateDto>.SuccessResponse(new RuntimeStateDto
        {
            StageOverrides = stageOverrides,
            DownstreamRuns = downstreamRuns,
            UploadedFiles = uploadedFiles,
            PackageStructure = packageStructure,
        });
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

            // TODO: 验证 ZIP 格式和必要文件

            // 查找关联的数字员工实例：优先查 Hiring 状态（首次导入），
            // 已导入过时实例状态为 InterningAi，同样匹配——确保重复导入时仍能返回 EmployeeId。
            var hiringInstance = await dbContext.Instances
                .Where(i => (i.Status == EmployeeStatus.Hiring || i.Status == EmployeeStatus.InterningAi)
                    && i.OwnerUserId == userIdentity.OwnerSubject
                    && i.BasedOnTemplateId == session.TemplateId)
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (hiringInstance is not null)
            {
                var employeeId = hiringInstance.InstanceId;

                // 仅在 Hiring 状态时才做状态迁移；InterningAi 表示已导入过，跳过重复更新
                if (hiringInstance.Status == EmployeeStatus.Hiring)
                {
                    var updateResult = await employeeRuntimeService.UpdateLifecycleAsync(
                        employeeId,
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
                            employeeId);
                    }
                    else
                    {
                        logger.LogWarning(
                            "员工状态更新失败（非致命错误）: HireId={HireId}, EmployeeId={EmployeeId}, Error={Error}",
                            hireId,
                            employeeId,
                            updateResult.Message);
                    }
                }
                else
                {
                    logger.LogInformation(
                        "员工实例已处于 {Status} 状态，跳过重复状态迁移: HireId={HireId}, EmployeeId={EmployeeId}",
                        hiringInstance.Status,
                        hireId,
                        employeeId);
                }
            }
            else
            {
                logger.LogWarning(
                    "未找到 Hiring/InterningAi 状态的员工实例: HireId={HireId}, TemplateId={TemplateId}",
                    hireId,
                    session.TemplateId);
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

            if (linkedStoreSkillIds is { Count: > 0 })
            {
                importMetadata["linked_store_skills"] = string.Join(",", linkedStoreSkillIds);
            }

            // 合并现有结构化数据
            var existingData = await hiringStageService.GetStructuredDataAsync(hireId, cancellationToken);
            var mergedData = new Dictionary<string, string?>(existingData, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in importMetadata)
            {
                mergedData[key] = value;
            }

            await hiringStageService.SaveStructuredDataAsync(hireId, mergedData, cancellationToken);

            logger.LogInformation("候选包导入成功: HireId={HireId}, 员工状态已更新为interning_ai", hireId);

            // 返回成功结果
            return ApiResponse<HiringFinalizeResultDto>.SuccessResponse(
                new HiringFinalizeResultDto(
                    HireId: hireId,
                    CurrentStage: HiringCollectionStage.ReadyForPackaging,
                    CollectionPhase: "finalized",
                    GeneratedFiles: Array.Empty<string>(),
                    DownloadUrl: $"/api/v1/hirings/{hireId}/artifacts/download",
                    EmployeeId: hiringInstance?.InstanceId),
                "候选包导入成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "导入候选包失败: HireId={HireId}, FileName={FileName}", hireId, fileName);
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(500, $"导入候选包失败: {ex.Message}");
        }
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

    /// <summary>
    /// 标记沙箱已完全初始化（模板包已上传，配置已设置，可以使用）。
    /// </summary>
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
}
