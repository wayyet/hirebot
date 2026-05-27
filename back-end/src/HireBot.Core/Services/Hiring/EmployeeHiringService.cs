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
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.StoreSkills;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    ITemplatePackageProvider templatePackageProvider,
    IDiscoveryRoleTemplatePackageProvider discoveryRoleTemplatePackageProvider,
    IWorkingTemplatePackageProvider workingTemplatePackageProvider,
    IDiscoveryRuleProvider discoveryRuleProvider,
    HiringStageCompletionEvaluator stageCompletionEvaluator,
    IHiringRuntimeStore hiringRuntimeStore,
    IKingCrabHttpClient kingCrabHttpClient,
    ISandboxService sandboxService,
    IHttpContextAccessor httpContextAccessor,
    IServiceScopeFactory serviceScopeFactory,
    HireBotDbContext dbContext,
    IHiringFileStore hiringFileStore,
    IInstanceArtifactCloneService instanceArtifactCloneService,
    IHiringArtifactPackageService artifactPackageService,
    IStoreSkillPackageDownloader storeSkillPackageDownloader,
    ISecretProtector secretProtector,
    IConfiguration configuration,
    ILogger<EmployeeHiringService> logger) : IEmployeeHiringService
{
    private const string EvaluationWorkspaceTemplateId = "evaluation-expert";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, byte> conversationInFlight = new(StringComparer.OrdinalIgnoreCase);
    public async Task<ApiResponse<HireTemplateResultDto>> HireAsync(
        string templateId,
        HireTemplateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(400, "templateId 不能为空");
        }

        request ??= new HireTemplateRequestDto();
        var (tenantId, operatorId) = ResolveTenantAndOperator(request.TenantId, request.OperatorId);

        var normalizedTemplateId = templateId.Trim();
        EmployeeTemplateDefinition? template;
        try
        {
            template = await templateDataProvider.GetByIdAsync(normalizedTemplateId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Template metadata unavailable from upstream source. TemplateId={TemplateId}", normalizedTemplateId);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(502, ex.Message);
        }

        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        TemplatePackageDefinition referenceTemplatePackage;
        TemplatePackageDefinition roleTemplatePackage;
        TemplatePackageDefinition workingTemplatePackage;
        DiscoverySkillDefinition discoverySkill;
        try
        {
            referenceTemplatePackage = await templatePackageProvider.LoadAsync(normalizedTemplateId, cancellationToken);
            roleTemplatePackage = await discoveryRoleTemplatePackageProvider.LoadAsync(cancellationToken);
            workingTemplatePackage = await workingTemplatePackageProvider.LoadAsync(cancellationToken);
            discoverySkill = await discoveryRuleProvider.LoadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load template/discovery assets. TemplateId={TemplateId}", normalizedTemplateId);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, "模板资产或 discovery skill 读取失败");
        }

        var ownerSubject = ResolveOwnerSubject(tenantId, operatorId);

        var existingInstance = await sandboxService.FindActiveByOwnerAndTemplateAsync(
            ownerSubject, normalizedTemplateId, "hiring", cancellationToken);
        if (existingInstance is not null)
        {
            if (string.Equals(existingInstance.State, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                await sandboxService.ResumeAsync(
                    new SandboxInstanceLookupRequestDto { SandboxId = existingInstance.SandboxId },
                    cancellationToken);
            }

            if (existingInstance.IsInitialized)
            {
                // 在复用前先 Refresh，验证沙箱在 OpenSandbox 中确实存活。
                // 若沙箱已被意外删除，RefreshAsync 会自动重建并将 IsInitialized 重置为 false，
                // 此时 fall-through 到下方的清理重建流程。
                var refreshed = await sandboxService.RefreshAsync(
                    new SandboxInstanceLookupRequestDto { SandboxId = existingInstance.SandboxId },
                    cancellationToken);
                if (refreshed.Success && refreshed.Data is not null)
                {
                    existingInstance = refreshed.Data;
                }

                if (!existingInstance.IsInitialized)
                {
                    logger.LogWarning(
                        "Existing initialized sandbox was rebuilt (sandbox deleted externally), reinitializing with same hireId. SandboxId={SandboxId}, HireId={HireId}",
                        existingInstance.SandboxId, existingInstance.ScopeKey);
                    // 沙箱被自动超时删除后，RefreshAsync 已用相同 ScopeKey (hireId) 重建并挂载原 PVC。
                    // 不走 reinitialize: 路径（会删除 PVC 并生成新 hireId），直接复用原 hireId 完成初始化，
                    // 确保工作区数据和 DB 会话历史关联不丢失。
                    return await ReinitializeRebuiltHireSandboxAsync(
                        existingInstance,
                        ownerSubject, tenantId, operatorId,
                        normalizedTemplateId, request.UseCase,
                        roleTemplatePackage, workingTemplatePackage,
                        discoverySkill, referenceTemplatePackage,
                        template, cancellationToken);
                }

                var existingHireId = existingInstance.ScopeKey;

                // 直接从持久化 runtime 读取 sessionId；缺失时再从 DB 会话表补全
                var existingRuntime = hiringRuntimeStore.Get(existingHireId);
                if (existingRuntime?.ExternalSystemConfig is null)
                {
                    var sandboxExternalConfig = DeserializeExternalSystemConfig(existingInstance.Metadata);
                    if (sandboxExternalConfig is not null && existingRuntime is not null)
                    {
                        existingRuntime = ApplyConversationProgressToTemplatePackage(existingRuntime with
                        {
                            ExternalSystemConfig = sandboxExternalConfig
                        });
                        hiringRuntimeStore.Upsert(existingRuntime);
                    }
                }

                var existingSessionId = existingRuntime?.SessionId;
                if (string.IsNullOrWhiteSpace(existingSessionId))
                {
                    existingSessionId = await dbContext.HiringSessions
                        .AsNoTracking()
                        .Where(s => s.HireId == existingHireId && s.DeletedAtUtc == null)
                        .OrderByDescending(s => s.CreatedAtUtc)
                        .Select(s => s.SessionId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                // 若持久化 runtime 中无运行时上下文，用 DB 补全的 sessionId 重建最小上下文，
                // 确保后续 syncConversationTurn 等调用能正常找到 HireId 对应的运行时
                if (existingRuntime is null && !string.IsNullOrWhiteSpace(existingSessionId))
                {
                    var restoredStageCompletion = stageCompletionEvaluator.Evaluate(
                        discoverySkill.StageRules,
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
                    hiringRuntimeStore.Upsert(new HiringRuntimeContext
                    {
                        HireId = existingHireId,
                        TemplateId = normalizedTemplateId,
                        TemplateName = template.Name,
                        OwnerSubject = ownerSubject,
                        TenantId = tenantId,
                        OperatorId = operatorId,
                        SandboxId = existingInstance.SandboxId,
                        SessionId = existingSessionId,
                        CurrentStage = HiringCollectionStage.Material,
                        CollectionPhase = HiringCollectionPhase.NotStarted,
                        IsConversationPaused = false,
                        IsConversationResponding = false,
                            RoleTemplatePackage = roleTemplatePackage,
                        WorkingTemplatePackage = workingTemplatePackage,
                        DiscoverySkill = discoverySkill,
                        StageCompletion = restoredStageCompletion,
                        ExternalSystemConfig = DeserializeExternalSystemConfig(existingInstance.Metadata)
                    });
                }

                // 沙箱 Running 时直接带回 gatewayEndpoint + sessionId，前端可跳过状态轮询和 startConversation() 调用
                var isRunning = string.Equals(existingInstance.State, "Running", StringComparison.OrdinalIgnoreCase);
                return ApiResponse<HireTemplateResultDto>.SuccessResponse(
                    new HireTemplateResultDto(
                        existingHireId,
                        existingInstance.SandboxId,
                        isRunning ? "READY" : existingInstance.State,
                        "continue_conversation",
                        SessionId: string.IsNullOrWhiteSpace(existingSessionId) ? null : existingSessionId,
                        GatewayEndpoint: isRunning ? existingInstance.GatewayEndpoint : null),
                    "已复用现有沙箱");
            }

            // 沙箱存在但从未完成初始化（非自动超时重建路径），清理后走正常创建流程。
            logger.LogInformation(
                "Existing sandbox is not initialized, cleaning up and provisioning fresh. OldSandboxId={OldSandboxId}, HireId={HireId}",
                existingInstance.SandboxId,
                existingInstance.ScopeKey);
            await sandboxService.DeleteAsync(
                new SandboxInstanceLookupRequestDto { SandboxId = existingInstance.SandboxId },
                cancellationToken);
        }

        var provisionResult = await ProvisionManagedHireSandboxAsync(
            sandboxRole: "hiring",
            ownerSubject,
            tenantId,
            operatorId,
            normalizedTemplateId,
            request.UseCase,
            cancellationToken);
        if (!provisionResult.Success || provisionResult.Data is null)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(provisionResult.Code, provisionResult.Message);
        }

        var call = RemoteCallResult<HireTemplateResultDto>.Ok(new HireTemplateResultDto(
            provisionResult.Data.HireId,
            provisionResult.Data.SandboxId,
            string.Equals(provisionResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase)
                ? "READY"
                : provisionResult.Data.State,
            "start_conversation"));

        var initialStageCompletion = stageCompletionEvaluator.Evaluate(
            discoverySkill.StageRules,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        hiringRuntimeStore.Upsert(new HiringRuntimeContext
        {
            HireId = provisionResult.Data.HireId,
            TemplateId = normalizedTemplateId,
            TemplateName = template.Name,
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            SandboxId = provisionResult.Data.SandboxId,
            SessionId = string.Empty,
            CurrentStage = HiringCollectionStage.Material,
            CollectionPhase = HiringCollectionPhase.NotStarted,
            IsConversationPaused = false,
            IsConversationResponding = false,
            RoleTemplatePackage = roleTemplatePackage,
            WorkingTemplatePackage = workingTemplatePackage,
            DiscoverySkill = discoverySkill,
            StructuredData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            Materials = [],
            StageCompletion = initialStageCompletion,
            IsTemplateUploadPending = false,
            TemplateUploadRetryCount = 0,
            TemplateUploadLastError = null,
            TemplateUploadLastAttemptAt = null
        });

        var conversationStartResponse = await sandboxService.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = call.Data!.HireId,
                SandboxRole = "hiring",
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = call.Data.SandboxId,
                SessionKey = "default"
            },
            cancellationToken);
        if (!conversationStartResponse.Success || conversationStartResponse.Data is null || string.IsNullOrWhiteSpace(conversationStartResponse.Data.SessionId))
        {
            logger.LogWarning(
                "Failed to create hiring session. HireId={HireId}, TemplateId={TemplateId}, StatusCode={StatusCode}, Message={Message}",
                call.Data.HireId,
                normalizedTemplateId,
                conversationStartResponse.Code,
                conversationStartResponse.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(
                conversationStartResponse.Code <= 0 ? 502 : conversationStartResponse.Code,
                string.IsNullOrWhiteSpace(conversationStartResponse.Message) ? "雇佣会话创建失败" : conversationStartResponse.Message);
        }

        hiringRuntimeStore.Upsert(hiringRuntimeStore.Get(call.Data.HireId) is { } runtimeWithNewSession
            ? runtimeWithNewSession with { SessionId = conversationStartResponse.Data.SessionId }
            : new HiringRuntimeContext
            {
                HireId = call.Data.HireId,
                TemplateId = normalizedTemplateId,
                TemplateName = template.Name,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = call.Data.SandboxId,
                SessionId = conversationStartResponse.Data.SessionId,
                CurrentStage = HiringCollectionStage.Material,
                CollectionPhase = HiringCollectionPhase.NotStarted,
                IsConversationPaused = false,
                IsConversationResponding = false,
                RoleTemplatePackage = roleTemplatePackage,
                WorkingTemplatePackage = workingTemplatePackage,
                DiscoverySkill = discoverySkill,
                StructuredData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                Materials = [],
                StageCompletion = initialStageCompletion
            });

        try
        {
            await PersistSessionAndSourceZipAsync(
                call.Data.HireId,
                conversationStartResponse.Data.SessionId,
                normalizedTemplateId,
                referenceTemplatePackage,
                ownerSubject,
                tenantId,
                operatorId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Persist session/source zip failed. HireId={HireId}, SessionId={SessionId}",
                call.Data.HireId,
                conversationStartResponse.Data.SessionId);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, "雇佣会话初始化持久化失败");
        }

        // 上传 employment-coach-conversation 角色模板包到沙箱，安装数字员工引导角色；
        // 模板包 ZIP 内已包含 skills/ 子目录，Gateway 上传后会自动装载技能，无需单独上传 skill archive；
        // 前端后续会把目标雇佣模板包作为媒体附件上传，触发 coach 解析引导流程
        var roleTemplateUploadResult = await UploadTemplatePackageAsync(
            call.Data.HireId,
            roleTemplatePackage,
            ownerSubject,
            cancellationToken);
        if (!roleTemplateUploadResult.Success)
        {
            logger.LogWarning(
                "Role template package upload failed. HireId={HireId}, PackageId={PackageId}, StatusCode={StatusCode}, Message={Message}",
                call.Data.HireId,
                roleTemplatePackage.PackageId,
                roleTemplateUploadResult.StatusCode,
                roleTemplateUploadResult.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(
                roleTemplateUploadResult.StatusCode <= 0 ? 502 : roleTemplateUploadResult.StatusCode,
                string.IsNullOrWhiteSpace(roleTemplateUploadResult.Message) ? "雇佣角色模板包上传失败" : roleTemplateUploadResult.Message);
        }

        // 上传 MCP 配置到沙箱，让沙箱内的数字员工可以访问配置的 MCP 服务；
        // 此步骤为非致命操作，失败时只记录警告，不阻断初始化流程
        var mcpConfig = ReadMcpConfig();
        if (mcpConfig is not null)
        {
            var mcpUploadResult = await UploadSandboxMcpConfigAsync(
                call.Data.HireId, ownerSubject, mcpConfig, cancellationToken);
            if (!mcpUploadResult.Success)
            {
                logger.LogWarning(
                    "MCP config upload failed (non-fatal). HireId={HireId}, StatusCode={StatusCode}, Message={Message}",
                    call.Data.HireId,
                    mcpUploadResult.StatusCode,
                    mcpUploadResult.Message);
            }
        }

        await SetSandboxInitializedAsync(provisionResult.Data.SandboxId, cancellationToken);

        logger.LogInformation(
            "Template hire setup completed. HireId={HireId}, TemplateId={TemplateId}, PackageId={PackageId}, PackageVersion={PackageVersion}, Owner={Owner}",
            call.Data.HireId,
            normalizedTemplateId,
            roleTemplatePackage.PackageId,
            roleTemplatePackage.PackageVersion,
            ownerSubject);

        return ApiResponse<HireTemplateResultDto>.SuccessResponse(
            call.Data with { SessionId = conversationStartResponse.Data.SessionId, TemplatePrimingRequired = true },
            "雇佣任务已创建");
    }

    /// <summary>
    /// 对已被自动删除并由 RefreshAsync 以相同 hireId/ScopeKey 重建的沙箱执行再初始化。
    /// 不删除 PVC、不变更 hireId，保留工作区数据；仅等待沙箱就绪后补全会话和模板上传等初始化步骤。
    /// </summary>
    private async Task<ApiResponse<HireTemplateResultDto>> ReinitializeRebuiltHireSandboxAsync(
        SandboxInstanceDto existingInstance,
        string ownerSubject,
        string tenantId,
        string operatorId,
        string normalizedTemplateId,
        string? useCase,
        TemplatePackageDefinition roleTemplatePackage,
        TemplatePackageDefinition workingTemplatePackage,
        DiscoverySkillDefinition discoverySkill,
        TemplatePackageDefinition referenceTemplatePackage,
        EmployeeTemplateDefinition template,
        CancellationToken cancellationToken)
    {
        var hireId = existingInstance.ScopeKey;

        // 等待沙箱就绪（RefreshAsync 刚创建的沙箱状态可能仍为 Creating）
        var readyResult = await WaitForManagedSandboxReadyAsync(existingInstance, cancellationToken);
        if (!readyResult.Success || readyResult.Data is null)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(readyResult.Code, readyResult.Message);
        }

        var readySandbox = readyResult.Data;
        var call = RemoteCallResult<HireTemplateResultDto>.Ok(new HireTemplateResultDto(
            hireId,
            readySandbox.SandboxId,
            string.Equals(readySandbox.State, "Running", StringComparison.OrdinalIgnoreCase) ? "READY" : readySandbox.State,
            "start_conversation"));

        var initialStageCompletion = stageCompletionEvaluator.Evaluate(
            discoverySkill.StageRules,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        hiringRuntimeStore.Upsert(new HiringRuntimeContext
        {
            HireId = hireId,
            TemplateId = normalizedTemplateId,
            TemplateName = template.Name,
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            SandboxId = readySandbox.SandboxId,
            SessionId = string.Empty,
            CurrentStage = HiringCollectionStage.Material,
            CollectionPhase = HiringCollectionPhase.NotStarted,
            IsConversationPaused = false,
            IsConversationResponding = false,
            RoleTemplatePackage = roleTemplatePackage,
            WorkingTemplatePackage = workingTemplatePackage,
            DiscoverySkill = discoverySkill,
            StructuredData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            Materials = [],
            StageCompletion = initialStageCompletion,
            IsTemplateUploadPending = false,
            TemplateUploadRetryCount = 0,
            TemplateUploadLastError = null,
            TemplateUploadLastAttemptAt = null
        });

        // EnsureSessionAsync 会从 DB 查找同 ScopeKey + SessionKey 的已有 sessionId 并复用，
        // 使重建后的沙箱能继续使用原会话上下文（PVC 中的 memory 数据与 sessionId 对应）
        var conversationStartResponse = await sandboxService.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = "hiring",
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = readySandbox.SandboxId,
                SessionKey = "default"
            },
            cancellationToken);
        if (!conversationStartResponse.Success || conversationStartResponse.Data is null || string.IsNullOrWhiteSpace(conversationStartResponse.Data.SessionId))
        {
            logger.LogWarning(
                "Failed to create hiring session during reinitialize. HireId={HireId}, TemplateId={TemplateId}, StatusCode={StatusCode}, Message={Message}",
                hireId, normalizedTemplateId, conversationStartResponse.Code, conversationStartResponse.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(
                conversationStartResponse.Code <= 0 ? 502 : conversationStartResponse.Code,
                string.IsNullOrWhiteSpace(conversationStartResponse.Message) ? "雇佣会话创建失败" : conversationStartResponse.Message);
        }

        hiringRuntimeStore.Upsert(hiringRuntimeStore.Get(hireId) is { } runtimeWithSession
            ? runtimeWithSession with { SessionId = conversationStartResponse.Data.SessionId }
            : new HiringRuntimeContext
            {
                HireId = hireId,
                TemplateId = normalizedTemplateId,
                TemplateName = template.Name,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = readySandbox.SandboxId,
                SessionId = conversationStartResponse.Data.SessionId,
                CurrentStage = HiringCollectionStage.Material,
                CollectionPhase = HiringCollectionPhase.NotStarted,
                IsConversationPaused = false,
                IsConversationResponding = false,
                RoleTemplatePackage = roleTemplatePackage,
                WorkingTemplatePackage = workingTemplatePackage,
                DiscoverySkill = discoverySkill,
                StructuredData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                Materials = [],
                StageCompletion = initialStageCompletion
            });

        try
        {
            // PersistSessionAndSourceZipAsync 内部会检测 hireId 对应的 session 是否已存在，
            // 重建场景下原 session 记录仍在 DB 中，方法会直接返回已有记录，不重复插入。
            await PersistSessionAndSourceZipAsync(
                hireId,
                conversationStartResponse.Data.SessionId,
                normalizedTemplateId,
                referenceTemplatePackage,
                ownerSubject, tenantId, operatorId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Persist session/source zip failed during reinitialize. HireId={HireId}, SessionId={SessionId}",
                hireId, conversationStartResponse.Data.SessionId);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, "雇佣会话初始化持久化失败");
        }

        // 重新上传角色模板包到新沙箱（原沙箱已删除，新沙箱需要重装）
        var roleTemplateUploadResult = await UploadTemplatePackageAsync(hireId, roleTemplatePackage, ownerSubject, cancellationToken);
        if (!roleTemplateUploadResult.Success)
        {
            logger.LogWarning(
                "Role template package upload failed during reinitialize. HireId={HireId}, PackageId={PackageId}, StatusCode={StatusCode}, Message={Message}",
                hireId, roleTemplatePackage.PackageId, roleTemplateUploadResult.StatusCode, roleTemplateUploadResult.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(
                roleTemplateUploadResult.StatusCode <= 0 ? 502 : roleTemplateUploadResult.StatusCode,
                string.IsNullOrWhiteSpace(roleTemplateUploadResult.Message) ? "雇佣角色模板包上传失败" : roleTemplateUploadResult.Message);
        }

        var mcpConfig = ReadMcpConfig();
        if (mcpConfig is not null)
        {
            var mcpUploadResult = await UploadSandboxMcpConfigAsync(hireId, ownerSubject, mcpConfig, cancellationToken);
            if (!mcpUploadResult.Success)
            {
                logger.LogWarning(
                    "MCP config upload failed (non-fatal) during reinitialize. HireId={HireId}, StatusCode={StatusCode}, Message={Message}",
                    hireId, mcpUploadResult.StatusCode, mcpUploadResult.Message);
            }
        }

        await SetSandboxInitializedAsync(readySandbox.SandboxId, cancellationToken);

        logger.LogInformation(
            "Sandbox reinitialize with same hireId completed. HireId={HireId}, NewSandboxId={SandboxId}",
            hireId, readySandbox.SandboxId);

        return ApiResponse<HireTemplateResultDto>.SuccessResponse(
            call.Data! with { SessionId = conversationStartResponse.Data.SessionId, TemplatePrimingRequired = true },
            "沙箱重建初始化完成");
    }

    private async Task<PersistedSourceZipInfo?> PersistSessionAndSourceZipAsync(
        string hireId,
        string sessionId,
        string templateId,
        TemplatePackageDefinition templatePackage,
        string ownerSubject,
        string tenantId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersistSessionAndSourceZip: HireId={HireId}, SessionId={SessionId}, PackageId={PackageId}, PackageVersion={PackageVersion}, SourceArchiveBytes={SourceBytes}",
            hireId,
            sessionId,
            templatePackage.PackageId,
            templatePackage.PackageVersion,
            templatePackage.SourceArchive?.LongLength ?? 0);

        var normalizedHireId = hireId.Trim();
        var normalizedSessionId = sessionId.Trim();

        var existing = await dbContext.HiringSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.HireId == normalizedHireId, cancellationToken);
        if (existing is not null)
        {
            var existingArtifact = await dbContext.HiringArtifacts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.SessionId == existing.SessionId && item.Kind == HiringArtifactPackageKinds.SourceZip,
                    cancellationToken);
            return existingArtifact is null
                ? null
                : new PersistedSourceZipInfo(
                    existingArtifact.FileName,
                    existingArtifact.StoragePath,
                    existingArtifact.Sha256,
                    existingArtifact.SizeBytes);
        }

        string? sourceFileName = null;
        string? sourceStoragePath = null;
        string? sourceSha = null;
        long? sourceSize = null;

        if (templatePackage.SourceArchive is not null && templatePackage.SourceArchive.Length > 0)
        {
            sourceSha = Convert.ToHexStringLower(SHA256.HashData(templatePackage.SourceArchive));
            sourceSize = templatePackage.SourceArchive.LongLength;
            await using var stream = new MemoryStream(templatePackage.SourceArchive, writable: false);
            sourceFileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";
            sourceStoragePath = await hiringFileStore.SaveAsync(
                normalizedSessionId,
                "source",
                sourceFileName,
                stream,
                cancellationToken);

            dbContext.HiringArtifacts.Add(new HiringArtifactEntity
            {
                SessionId = normalizedSessionId,
                Kind = HiringArtifactPackageKinds.SourceZip,
                LogicalPath = $"source/{sourceFileName}",
                FileName = sourceFileName,
                SizeBytes = sourceSize.Value,
                Sha256 = sourceSha,
                StoragePath = sourceStoragePath,
                IsFinal = false,
                IsArchived = false,
                UploadedAtUtc = DateTimeOffset.UtcNow
            });
        }

        dbContext.HiringSessions.Add(new HiringSessionEntity
        {
            SessionId = normalizedSessionId,
            HireId = normalizedHireId,
            TemplateId = templateId.Trim(),
            PackageId = string.IsNullOrWhiteSpace(templatePackage.PackageId) ? null : templatePackage.PackageId.Trim(),
            PackageVersion = string.IsNullOrWhiteSpace(templatePackage.PackageVersion) ? null : templatePackage.PackageVersion.Trim(),
            PackageHash = string.IsNullOrWhiteSpace(templatePackage.PackageHash) ? null : templatePackage.PackageHash.Trim(),
            SourceZipSha256 = sourceSha,
            SourceZipStoragePath = sourceStoragePath,
            SourceZipSizeBytes = sourceSize,
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        dbContext.HiringAuditLogs.Add(new HiringAuditLogEntity
        {
            SessionId = normalizedSessionId,
            HireId = normalizedHireId,
            Action = "create_session",
            Actor = ownerSubject,
            Ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            BeforeSha256 = null,
            AfterSha256 = sourceSha,
            DetailJson = $"{{\"templateId\":\"{templateId}\"}}",
            TimestampUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "PersistSessionAndSourceZip completed. HireId={HireId}, SessionId={SessionId}, SourceZipSha256={Sha}, SourceZipPath={Path}",
            hireId,
            sessionId,
            sourceSha ?? string.Empty,
            sourceStoragePath ?? string.Empty);

        return string.IsNullOrWhiteSpace(sourceStoragePath) || string.IsNullOrWhiteSpace(sourceFileName) || string.IsNullOrWhiteSpace(sourceSha) || sourceSize is null
            ? null
            : new PersistedSourceZipInfo(
                sourceFileName,
                sourceStoragePath,
                sourceSha,
                sourceSize.Value);
    }

    /// <summary>
    /// 获取雇佣流程的沙箱状态。
    /// 前端通过轮询此接口等待沙箱就绪（status == "READY"）后再调用 StartConversation。
    /// 注意：OpenSandbox 的就绪状态为 "Running"，此方法会将其映射为前端期望的 "READY"。
    /// </summary>
    public async Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringStatusDto>.ErrorResponse(400, error);
        }

        var ownerContext = ResolveOwnerContextByHireId(normalizedHireId);
        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        var refreshResult = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto
            {
                SandboxId = runtimeContext?.SandboxId,
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = normalizedHireId,
                SandboxRole = ResolveSandboxRole(normalizedHireId),
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                TemplateId = ownerContext.TemplateId
            },
            cancellationToken);
        if (!refreshResult.Success || refreshResult.Data is null)
        {
            return ApiResponse<HiringStatusDto>.ErrorResponse(refreshResult.Code, refreshResult.Message);
        }

        // RefreshAsync 可能在沙箱被外部删除后重建了沙箱（新 SandboxId），同步到内存上下文。
        // 如果沙箱未初始化（被删除后重建的空壳），则触发重新初始化（上传模板包 + 冷启动提示词）。
        if (runtimeContext is not null)
        {
            if (!string.Equals(runtimeContext.SandboxId, refreshResult.Data.SandboxId, StringComparison.Ordinal))
            {
                runtimeContext = runtimeContext with { SandboxId = refreshResult.Data.SandboxId };
                hiringRuntimeStore.Upsert(runtimeContext);
            }

            if (!refreshResult.Data.IsInitialized)
            {
                runtimeContext = await EnsureSandboxReinitializedAsync(runtimeContext, cancellationToken);
            }

            runtimeContext = await EnsureExternalSystemConfigHydratedAsync(runtimeContext, cancellationToken);
        }

        runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken) ?? runtimeContext;

        // 前端轮询等待 "READY" 信号。
        // OpenSandbox 就绪状态为 "Running" + GatewayEndpoint 非空，此时对前端统一映射为 "READY"。
        var sandboxState = refreshResult.Data.State;
        var frontendStatus = string.Equals(sandboxState, "Running", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint)
            ? "READY"
            : sandboxState;

        return ApiResponse<HiringStatusDto>.SuccessResponse(
            new HiringStatusDto(
                normalizedHireId,
                refreshResult.Data.SandboxId,
                frontendStatus,
                GatewayEndpoint: refreshResult.Data.GatewayEndpoint,
                ErrorCode: null,
                ErrorMessage: refreshResult.Data.LastError,
                CollectionPhase: runtimeContext?.CollectionPhase,
                CurrentStage: runtimeContext?.CurrentStage));
    }

    public async Task<ApiResponse<StartHiringConversationResultDto>> StartConversationAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(400, error);
        }

        var ownerContext = ResolveOwnerContextByHireId(normalizedHireId);

        // 如果沙箱被删除后重建为空壳，先完成初始化（上传模板包 + 冷启动提示词）
        var runtimeBeforeSession = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeBeforeSession is not null)
        {
            runtimeBeforeSession = await EnsureSandboxReinitializedAsync(runtimeBeforeSession, cancellationToken);
        }

        var sessionResult = await sandboxService.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = normalizedHireId,
                SandboxRole = ResolveSandboxRole(normalizedHireId),
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                SandboxId = runtimeBeforeSession?.SandboxId ?? hiringRuntimeStore.Get(normalizedHireId)?.SandboxId,
                SessionKey = "default"
            },
            cancellationToken);

        if (!sessionResult.Success || sessionResult.Data is null)
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(sessionResult.Code, sessionResult.Message);
        }

        var call = RemoteCallResult<StartHiringConversationResultDto>.Ok(sessionResult.Data);

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is not null)
        {
            runtimeContext = ApplyWorkflowProgress(runtimeContext with
            {
                SessionId = call.Data!.SessionId
            });
            hiringRuntimeStore.Upsert(runtimeContext);
            var requiresAudit = BuildLocalStagePreview(
                normalizedHireId,
                runtimeContext.DiscoverySkill,
                runtimeContext.StageCompletion,
                runtimeContext.CurrentStage,
                runtimeContext.CollectionPhase,
                runtimeContext.StructuredData,
                summaryOverride: null).ReadyForAudit;
            call = RemoteCallResult<StartHiringConversationResultDto>.Ok(call.Data with
            {
                CurrentStage = runtimeContext.CurrentStage,
                RequiresAudit = requiresAudit,
                StageSkills = BuildStageSkills(runtimeContext.DiscoverySkill),
                IsConversationPaused = runtimeContext.IsConversationPaused,
                IsConversationResponding = IsConversationResponding(normalizedHireId, runtimeContext)
            });
        }

        return ApiResponse<StartHiringConversationResultDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<StartHiringConversationResultDto>> ResetConversationAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(400, error);
        }

        var ownerContext = ResolveOwnerContextByHireId(normalizedHireId);
        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        var sandboxId = runtimeContext?.SandboxId;

        // 使用唯一 session key 强制创建新会话
        var newSessionKey = $"default-reset-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var sessionResult = await sandboxService.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = normalizedHireId,
                SandboxRole = ResolveSandboxRole(normalizedHireId),
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                SandboxId = sandboxId,
                SessionKey = newSessionKey
            },
            cancellationToken);

        if (!sessionResult.Success || sessionResult.Data is null)
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(sessionResult.Code, sessionResult.Message);
        }

        if (runtimeContext is not null)
        {
            runtimeContext = runtimeContext with
            {
                SessionId = sessionResult.Data.SessionId,
                HandoffItems = [],
                Materials = [],
                StructuredData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                CurrentStage = HiringCollectionStage.Material,
                CollectionPhase = HiringCollectionPhase.InProgress,
                IsConversationPaused = false,
                LatestDispatches = [],
                ConfigGovernance = null
            };
            hiringRuntimeStore.Upsert(runtimeContext);
        }

        return ApiResponse<StartHiringConversationResultDto>.SuccessResponse(sessionResult.Data);
    }

    public Task<ApiResponse<HiringConversationControlResultDto>> PauseConversationAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        return SetConversationPausedAsync(hireId, isPaused: true);
    }

    public Task<ApiResponse<HiringConversationControlResultDto>> ResumeConversationAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        return SetConversationPausedAsync(hireId, isPaused: false);
    }

    public async Task<ApiResponse<HiringConversationResultDto>> SendConversationMessageAsync(
        string hireId,
        HiringConversationMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var idError))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, idError);
        }

        if (request is null ||
            (string.IsNullOrWhiteSpace(request.Content) &&
             (request.Materials is null || request.Materials.Count == 0)))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, "content 与 materials 不能同时为空");
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext?.IsConversationPaused == true)
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "对话已暂停，请先恢复后再继续发送消息");
        }

        if (!conversationInFlight.TryAdd(normalizedHireId, 0))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "上一轮回复仍在生成中，请稍候");
        }

        try
        {
            runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
            if (runtimeContext?.IsConversationPaused == true)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "对话已暂停，请先恢复后再继续发送消息");
            }

            if (runtimeContext is not null)
            {
                runtimeContext = runtimeContext with { IsConversationResponding = true };
                hiringRuntimeStore.Upsert(runtimeContext);
            }

            if (runtimeContext is null)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(404, "雇佣上下文不存在，请重新发起流程");
            }

            // 如果沙箱被删除后重建为空壳，先完成初始化再发送消息
            runtimeContext = await EnsureSandboxReinitializedAsync(runtimeContext, cancellationToken) ?? runtimeContext;

            var requestMaterials = BuildMaterialsFromRequest(request);
            if (HiringWorkflowSupport.ContainsSensitiveValue(request.Content))
            {
                var now = DateTimeOffset.UtcNow;
                var assistantMessage = new HiringConversationMessageDto(
                    $"assistant-{Guid.NewGuid():N}",
                    "assistant",
                    "检测到你在对话里输入了凭据或密钥，这类信息不会进入会话。请改用右侧凭据表单提交。",
                    now);

                runtimeContext = runtimeContext with
                {
                    Materials = MergeMaterials(runtimeContext.Materials, requestMaterials)
                };
                runtimeContext = ApplyWorkflowProgress(runtimeContext);
                runtimeContext = ApplyConversationProgressToTemplatePackage(runtimeContext);
                if (ShouldPersistArtifactPackages(runtimeContext))
                {
                    await PersistIntermediatePackageAsync(runtimeContext, cancellationToken);
                }

                hiringRuntimeStore.Upsert(runtimeContext);
                var blockedPreview = BuildLocalStagePreview(
                    normalizedHireId,
                    runtimeContext.DiscoverySkill,
                    runtimeContext.StageCompletion,
                    runtimeContext.CurrentStage,
                    runtimeContext.CollectionPhase,
                    runtimeContext.StructuredData,
                    assistantMessage.Content);

                return ApiResponse<HiringConversationResultDto>.SuccessResponse(
                    new HiringConversationResultDto(
                        normalizedHireId,
                        runtimeContext.SessionId,
                        runtimeContext.CurrentStage,
                        blockedPreview.ReadyForAudit,
                        assistantMessage,
                        blockedPreview,
                        runtimeContext.IsConversationPaused,
                        true));
            }

            var sendResponse = await SendSandboxConversationMessageAsync(
                runtimeContext,
                request.Content,
                requestMaterials,
                cancellationToken);

            if (!sendResponse.Success || sendResponse.Data is null)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(sendResponse.Code, sendResponse.Message);
            }

            runtimeContext = runtimeContext with { SessionId = sendResponse.Data.SessionId };
            return await ProcessConversationTurnAsync(
                runtimeContext,
                request.Content?.Trim(),
                sendResponse.Data.AssistantMessage.Content,
                requestMaterials,
                request.StructuredAnswers?.ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
                cancellationToken);
        }
        finally
        {
            conversationInFlight.TryRemove(normalizedHireId, out _);

            var latestContext = hiringRuntimeStore.Get(normalizedHireId);
            if (latestContext?.IsConversationResponding == true)
            {
                hiringRuntimeStore.Upsert(latestContext with { IsConversationResponding = false });
            }
        }
    }

    /// <summary>
    /// 前端通过 WebSocket 直连沙箱完成一轮对话后，调用此接口将对话轮次同步到后端，
    /// 使后端工作流引擎能够解析 AI 结构化标签、推进阶段状态、执行 dispatch 命令等。
    /// </summary>
    public async Task<ApiResponse<HiringConversationResultDto>> SyncConversationTurnAsync(
        string hireId,
        HiringConversationSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var idError))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, idError);
        }

        if (request is null || (string.IsNullOrWhiteSpace(request.UserMessage) && string.IsNullOrWhiteSpace(request.AssistantReply)))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, "userMessage 与 assistantReply 不能同时为空");
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(404, "雇佣上下文不存在，请重新发起流程");
        }

        if (runtimeContext.IsConversationPaused)
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "对话已暂停，请先恢复后再继续发送消息");
        }

        if (!conversationInFlight.TryAdd(normalizedHireId, 0))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "上一轮回复仍在生成中，请稍候");
        }

        try
        {
            runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
            if (runtimeContext is null)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(404, "雇佣上下文不存在，请重新发起流程");
            }

            if (runtimeContext.IsConversationPaused)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "对话已暂停，请先恢复后再继续发送消息");
            }

            runtimeContext = runtimeContext with { IsConversationResponding = true };
            hiringRuntimeStore.Upsert(runtimeContext);

            var materials = request.Materials ?? Array.Empty<HiringConversationMaterialDto>();

            if (HiringWorkflowSupport.ContainsSensitiveValue(request.UserMessage))
            {
                var now = DateTimeOffset.UtcNow;
                var assistantMessage = new HiringConversationMessageDto(
                    $"assistant-{Guid.NewGuid():N}",
                    "assistant",
                    "检测到你在对话里输入了凭据或密钥，这类信息不会进入会话。请改用右侧凭据表单提交。",
                    now);

                runtimeContext = runtimeContext with
                {
                    Materials = MergeMaterials(runtimeContext.Materials, materials)
                };
                runtimeContext = ApplyWorkflowProgress(runtimeContext);
                runtimeContext = ApplyConversationProgressToTemplatePackage(runtimeContext);
                if (ShouldPersistArtifactPackages(runtimeContext))
                {
                    await PersistIntermediatePackageAsync(runtimeContext, cancellationToken);
                }

                hiringRuntimeStore.Upsert(runtimeContext);
                var blockedPreview = BuildLocalStagePreview(
                    normalizedHireId,
                    runtimeContext.DiscoverySkill,
                    runtimeContext.StageCompletion,
                    runtimeContext.CurrentStage,
                    runtimeContext.CollectionPhase,
                    runtimeContext.StructuredData,
                    assistantMessage.Content);

                return ApiResponse<HiringConversationResultDto>.SuccessResponse(
                    new HiringConversationResultDto(
                        normalizedHireId,
                        runtimeContext.SessionId,
                        runtimeContext.CurrentStage,
                        blockedPreview.ReadyForAudit,
                        assistantMessage,
                        blockedPreview,
                        runtimeContext.IsConversationPaused,
                        true));
            }

            return await ProcessConversationTurnAsync(
                runtimeContext,
                string.IsNullOrWhiteSpace(request.UserMessage) ? null : request.UserMessage.Trim(),
                request.AssistantReply,
                materials,
                structuredAnswers: null,
                cancellationToken);
        }
        finally
        {
            conversationInFlight.TryRemove(normalizedHireId, out _);

            var latestContext = hiringRuntimeStore.Get(normalizedHireId);
            if (latestContext?.IsConversationResponding == true)
            {
                hiringRuntimeStore.Upsert(latestContext with { IsConversationResponding = false });
            }
        }
    }

    public async Task<ApiResponse<HiringConversationTimelineDto>> GetConversationTimelineAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(404, "雇佣上下文不存在，请重新发起流程");
        }

        var timelinePreview = BuildLocalStagePreview(
            normalizedHireId,
            runtimeContext.DiscoverySkill,
            runtimeContext.StageCompletion,
            runtimeContext.CurrentStage,
            runtimeContext.CollectionPhase,
            runtimeContext.StructuredData,
            summaryOverride: null);

        return ApiResponse<HiringConversationTimelineDto>.SuccessResponse(
            new HiringConversationTimelineDto(
                normalizedHireId,
                runtimeContext.SessionId,
                runtimeContext.CurrentStage,
                timelinePreview.ReadyForAudit,
                runtimeContext.CollectionPhase,
                [],
                BuildStageSkills(runtimeContext.DiscoverySkill)));
    }

    public async Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(
        string hireId,
        string? stage,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringStagePreviewDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringStagePreviewDto>.ErrorResponse(404, "雇佣上下文不存在，请重新发起流程");
        }

        var targetStage = string.IsNullOrWhiteSpace(stage)
            ? runtimeContext.CurrentStage
            : NormalizeRequestedStage(stage);
        var preview = BuildLocalStagePreview(
            normalizedHireId,
            runtimeContext.DiscoverySkill,
            runtimeContext.StageCompletion,
            targetStage,
            runtimeContext.CollectionPhase,
            runtimeContext.StructuredData,
            summaryOverride: null);

        return ApiResponse<HiringStagePreviewDto>.SuccessResponse(preview);
    }

    public async Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(
        string hireId,
        HiringAuditDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var idError))
        {
            return ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, idError);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Stage) || string.IsNullOrWhiteSpace(request.Decision))
        {
            return ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, "stage 与 decision 为必填项");
        }

        var call = await SendForJsonAsync<HiringAuditDecisionResultDto>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/audit-decisions",
            request,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is not null)
        {
            runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken) ?? runtimeContext;
            call = RemoteCallResult<HiringAuditDecisionResultDto>.Ok(call.Data with
            {
                CurrentStage = runtimeContext.CurrentStage,
                CollectionPhase = runtimeContext.CollectionPhase
            });
        }

        return ApiResponse<HiringAuditDecisionResultDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<IReadOnlyList<HiringAuditLogDto>>.ErrorResponse(400, error);
        }

        return ApiResponse<IReadOnlyList<HiringAuditLogDto>>.SuccessResponse([]);
    }

    public async Task<ApiResponse<HiringFinalizeResultDto>> ImportPackageAsync(
        string hireId,
        Stream packageStream,
        string fileName,
        IReadOnlyList<string>? linkedStoreSkillIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(409, "本地雇佣上下文不存在，请重新发起雇佣流程");
        }

        runtimeContext = await EnsureExternalSystemConfigHydratedAsync(runtimeContext, cancellationToken);

        // 将前端直传的产物包读取为字节数组
        byte[] packageBytes;
        using (var ms = new MemoryStream())
        {
            await packageStream.CopyToAsync(ms, cancellationToken);
            packageBytes = ms.ToArray();
        }

        if (packageBytes.Length == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(400, "上传的产物包为空");
        }

        var extractedArtifacts = ExtractZipEntries(packageBytes);
        if (extractedArtifacts.Count == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(422, "产物包为空或无法解析，请确认上传的是有效 ZIP 文件");
        }

        // 用户在前端 TODO 面板关联的 store skill：先从 ncrew-builder 拉取并解压，作为产物的"中层"基底。
        // 优先级：沙箱产物（最高）> store skill > 原始模板包（最低），保证用户显式选择的技能不会被陈旧模板覆盖。
        IReadOnlyDictionary<string, byte[]> storeSkillArtifacts;
        try
        {
            storeSkillArtifacts = linkedStoreSkillIds is { Count: > 0 }
                ? await storeSkillPackageDownloader.DownloadSkillsAsync(linkedStoreSkillIds, cancellationToken)
                : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Linked store skills download failed; proceeding without them. HireId={HireId}", normalizedHireId);
            storeSkillArtifacts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }

        var mergedArtifacts = MergeTemplatePackageArtifacts(
            extractedArtifacts,
            storeSkillArtifacts,
            runtimeContext.WorkingTemplatePackage);
        OverlayManagedExternalPackageArtifacts(mergedArtifacts, runtimeContext.ExternalSystemConfig);
        if (mergedArtifacts.Count == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(422, "产物包合并后无有效文件");
        }

        // 创建数字员工实例（首次调用时）
        // 直接从 DB 持久化的 runtimeContext 读取所有者信息，保证重启后依然有效。
        string? employeeId = runtimeContext.EmployeeId;
        if (string.IsNullOrWhiteSpace(employeeId) && !string.IsNullOrWhiteSpace(runtimeContext.TemplateId))
        {
            var capabilities = (await templateDataProvider.GetByIdAsync(runtimeContext.TemplateId, cancellationToken))?.CoreAbilities ?? [];
            using var scope = serviceScopeFactory.CreateScope();
            var employeeRuntimeService = scope.ServiceProvider.GetRequiredService<IEmployeeRuntimeService>();
            var createResponse = await employeeRuntimeService.CreateFromHireAsync(
                new CreateEmployeeFromHireRequestDto(
                    HireId: normalizedHireId,
                    TemplateId: runtimeContext.TemplateId,
                    TemplateName: runtimeContext.TemplateName,
                    OwnerSubject: runtimeContext.OwnerSubject,
                    TenantId: runtimeContext.TenantId,
                    OperatorId: runtimeContext.OperatorId,
                    Capabilities: capabilities),
                cancellationToken);

            if (createResponse.Success && createResponse.Data is not null)
            {
                employeeId = createResponse.Data.EmployeeId;
            }
        }

        // 存储数字员工 artifacts
        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            try
            {
                var storedArtifacts = await instanceArtifactCloneService.StoreDepartmentArtifactsAsync(
                    employeeId,
                    mergedArtifacts,
                    cancellationToken);
                var instance = await dbContext.Instances.FirstOrDefaultAsync(
                    item => item.InstanceId == employeeId,
                    cancellationToken);
                if (instance is not null)
                {
                    instance.CurrentVersion = storedArtifacts.CurrentVersion;
                    instance.UpdatedAt = DateTimeOffset.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist imported instance artifacts. EmployeeId={EmployeeId}", employeeId);
            }
        }

        if (ShouldPersistArtifactPackages(runtimeContext))
        {
            await artifactPackageService.PersistFinalPackageAsync(
                new HiringArtifactPackagePersistRequestDto(
                    runtimeContext.HireId,
                    runtimeContext.SessionId,
                    BuildFinalPackageFileName(normalizedHireId, fileName),
                    mergedArtifacts),
                cancellationToken);
        }

        runtimeContext = runtimeContext with
        {
            CurrentStage = HiringCollectionStage.ReadyForPackaging,
            CollectionPhase = HiringCollectionPhase.Finalized,
            EmployeeId = employeeId
        };
        runtimeContext = ApplyWorkflowProgress(runtimeContext);
        hiringRuntimeStore.Upsert(runtimeContext);

        var result = new HiringFinalizeResultDto(
            HireId: normalizedHireId,
            CurrentStage: runtimeContext.CurrentStage,
            CollectionPhase: runtimeContext.CollectionPhase,
            GeneratedFiles: mergedArtifacts.Keys.ToArray(),
            DownloadUrl: $"/api/v1/hirings/{normalizedHireId}/artifacts/download",
            EmployeeId: employeeId);

        return ApiResponse<HiringFinalizeResultDto>.SuccessResponse(result, "交付物已导入");
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(400, error));
        }

        return artifactPackageService.BuildFinalPackageDownloadAsync(normalizedHireId, cancellationToken);
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(
        string hireId,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(400, error));
        }

        return artifactPackageService.BuildFinalPackageFileDownloadAsync(
            normalizedHireId,
            artifactName,
            cancellationToken);
    }

    public async Task<ApiResponse<bool>> SaveConversationCacheAsync(
        string hireId,
        JsonElement cache,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<bool>.ErrorResponse(400, error);
        }

        var entity = await dbContext.HiringRuntimeStates
            .Where(e => e.HireId == normalizedHireId)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return ApiResponse<bool>.ErrorResponse(404, "未找到该雇佣流程的运行时状态");
        }

        entity.ConversationCacheJson = cache.GetRawText();
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<JsonElement?>> GetConversationCacheAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<JsonElement?>.ErrorResponse(400, error);
        }

        var entity = await dbContext.HiringRuntimeStates
            .AsNoTracking()
            .Where(e => e.HireId == normalizedHireId)
            .Select(e => e.ConversationCacheJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return ApiResponse<JsonElement?>.ErrorResponse(404, "未找到该雇佣流程的运行时状态");
        }

        var json = JsonSerializer.Deserialize<JsonElement>(entity);
        return ApiResponse<JsonElement?>.SuccessResponse(json);
    }

}
