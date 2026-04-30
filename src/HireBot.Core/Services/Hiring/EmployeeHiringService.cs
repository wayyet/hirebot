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
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    ITemplatePackageProvider templatePackageProvider,
    IDiscoveryRuleProvider discoveryRuleProvider,
    ISystemSkillRegistry systemSkillRegistry,
    HiringStageCompletionEvaluator stageCompletionEvaluator,
    IHiringRuntimeStore hiringRuntimeStore,
    IKingCrabHttpClient kingCrabHttpClient,
    ISandboxService sandboxService,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IServiceScopeFactory serviceScopeFactory,
    HireBotDbContext dbContext,
    IHiringFileStore hiringFileStore,
    IInstanceArtifactCloneService instanceArtifactCloneService,
    IHiringArtifactPackageService artifactPackageService,
    ILogger<EmployeeHiringService> logger) : IEmployeeHiringService
{
    private const string DefaultConversationKickoffPrompt = "你是雇佣流程助手。请先根据当前阶段提出第一个关键问题，引导用户完善模板包内容。";
    private const string EvaluationSkillId = "evaluation-expert";
    private const string EvaluationSkillVersion = "2.1.0";
    private const string EvaluationWorkspaceTemplateId = "evaluation-expert";
    private const string EvaluationWorkspaceTemplateName = "Evaluation Expert";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, HireOwnerContext> hireOwners = new(StringComparer.OrdinalIgnoreCase);
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

        TemplatePackageDefinition templatePackage;
        DiscoverySkillDefinition discoverySkill;
        try
        {
            templatePackage = await templatePackageProvider.LoadAsync(normalizedTemplateId, cancellationToken);
            discoverySkill = await discoveryRuleProvider.LoadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load template/discovery assets. TemplateId={TemplateId}", normalizedTemplateId);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, "模板资产或 discovery skill 读取失败");
        }

        var ownerSubject = ResolveOwnerSubject(tenantId, operatorId);
        var provisionResult = await ProvisionManagedHireSandboxAsync(
            sandboxRole: "hiring",
            ownerSubject,
            tenantId,
            operatorId,
            request.UseCase,
            cancellationToken);
        if (!provisionResult.Success || provisionResult.Data is null)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(provisionResult.Code, provisionResult.Message);
        }

        var call = RemoteCallResult<HireTemplateResultDto>.Ok(new HireTemplateResultDto(
            provisionResult.Data.HireId,
            provisionResult.Data.SandboxId,
            // ProvisionManagedHireSandboxAsync 已同步等待沙箱就绪，此时 State 为 "Running"。
            // 对前端统一映射为 "READY"，使前端轮询可以立即跳过等待。
            string.Equals(provisionResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase)
                ? "READY"
                : provisionResult.Data.State,
            "start_conversation"));

        var initialStageCompletion = stageCompletionEvaluator.Evaluate(
            discoverySkill.StageRules,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        hireOwners[provisionResult.Data.HireId] = new HireOwnerContext(
            OwnerSubject: ownerSubject,
            TenantId: tenantId,
            OperatorId: operatorId,
            TemplateId: normalizedTemplateId,
            TemplateName: template.Name,
            EmployeeId: null);

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
            CurrentStage = HiringCollectionStage.Goal,
            CollectionPhase = HiringCollectionPhase.NotStarted,
            IsConversationPaused = false,
            IsConversationResponding = false,
            TemplatePackage = templatePackage,
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
                ScopeKey = call.Data.HireId,
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

        hiringRuntimeStore.Upsert(hiringRuntimeStore.Get(call.Data.HireId) is { } existingRuntime
            ? existingRuntime with { SessionId = conversationStartResponse.Data.SessionId }
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
                CurrentStage = HiringCollectionStage.Goal,
                CollectionPhase = HiringCollectionPhase.NotStarted,
                IsConversationPaused = false,
                IsConversationResponding = false,
                TemplatePackage = templatePackage,
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
                templatePackage,
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

        var systemSkillCall = await UploadDiscoverySystemSkillAsync(
            call.Data.HireId,
            discoverySkill,
            ownerSubject,
            cancellationToken);
        if (!systemSkillCall.Success || systemSkillCall.Data is null)
        {
            logger.LogWarning(
                "Discovery system skill upload failed. HireId={HireId}, TemplateId={TemplateId}, StatusCode={StatusCode}, Message={Message}",
                call.Data.HireId,
                normalizedTemplateId,
                systemSkillCall.StatusCode,
                systemSkillCall.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(systemSkillCall.StatusCode, systemSkillCall.Message);
        }

        var templatePackageCall = await UploadTemplatePackageAsync(
            call.Data.HireId,
            templatePackage,
            discoverySkill,
            ownerSubject,
            cancellationToken);
        if (!templatePackageCall.Success || templatePackageCall.Data is null)
        {
            logger.LogWarning(
                "Template package upload failed. HireId={HireId}, TemplateId={TemplateId}, StatusCode={StatusCode}, Message={Message}",
                call.Data.HireId,
                normalizedTemplateId,
                templatePackageCall.StatusCode,
                templatePackageCall.Message);
            if (hiringRuntimeStore.Get(call.Data.HireId) is { } runtimeWithSession)
            {
                hiringRuntimeStore.Upsert(runtimeWithSession with
                {
                    IsTemplateUploadPending = false,
                    TemplateUploadRetryCount = 0,
                    TemplateUploadLastError = templatePackageCall.Message,
                    TemplateUploadLastAttemptAt = DateTimeOffset.UtcNow
                });
            }

            return ApiResponse<HireTemplateResultDto>.ErrorResponse(
                templatePackageCall.StatusCode <= 0 ? 502 : templatePackageCall.StatusCode,
                templatePackageCall.Message);
        }

        if (hiringRuntimeStore.Get(call.Data.HireId) is { } uploadedRuntime)
        {
            hiringRuntimeStore.Upsert(uploadedRuntime with
            {
                IsTemplateUploadPending = false,
                TemplateUploadRetryCount = 0,
                TemplateUploadLastError = null,
                TemplateUploadLastAttemptAt = DateTimeOffset.UtcNow
            });
        }

        var uploadedPackageId = templatePackageCall.Data?.PackageId ?? templatePackage.PackageId;
        var uploadedPackageVersion = templatePackageCall.Data?.PackageVersion ?? templatePackage.PackageVersion;
        logger.LogInformation(
            "Template hire submitted to KingCrew with discovery system skill and template package uploaded. HireId={HireId}, TemplateId={TemplateId}, SkillId={SkillId}, SkillVersion={SkillVersion}, PackageId={PackageId}, PackageVersion={PackageVersion}, Owner={Owner}",
            call.Data.HireId,
            normalizedTemplateId,
            systemSkillCall.Data.SkillId,
            systemSkillCall.Data.SkillVersion,
            uploadedPackageId,
            uploadedPackageVersion,
            ownerSubject);

        return ApiResponse<HireTemplateResultDto>.SuccessResponse(
            call.Data with { SessionId = conversationStartResponse.Data.SessionId },
            "雇佣任务已创建");
    }

    private async Task PersistSessionAndSourceZipAsync(
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
            return;
        }

        string? sourceStoragePath = null;
        string? sourceSha = null;
        long? sourceSize = null;

        if (templatePackage.SourceArchive is not null && templatePackage.SourceArchive.Length > 0)
        {
            sourceSha = Convert.ToHexStringLower(SHA256.HashData(templatePackage.SourceArchive));
            sourceSize = templatePackage.SourceArchive.LongLength;
            await using var stream = new MemoryStream(templatePackage.SourceArchive, writable: false);
            var fileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";
            sourceStoragePath = await hiringFileStore.SaveAsync(
                normalizedSessionId,
                "source",
                fileName,
                stream,
                cancellationToken);

            dbContext.HiringArtifacts.Add(new HiringArtifactEntity
            {
                SessionId = normalizedSessionId,
                Kind = HiringArtifactPackageKinds.SourceZip,
                LogicalPath = $"source/{fileName}",
                FileName = fileName,
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
    }

    public async Task<ApiResponse<HireTemplateResultDto>> CreateEvaluationWorkspaceAsync(
        string targetHireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(targetHireId, out var normalizedTargetHireId, out var error))
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(400, error);
        }

        var ownerContext = ResolveOwnerContextForEvaluation(normalizedTargetHireId);
        var useCase = $"evaluation-workspace-for:{normalizedTargetHireId}";
        var provisionResult = await ProvisionManagedHireSandboxAsync(
            sandboxRole: "evaluation-evaluator",
            ownerContext.OwnerSubject,
            ownerContext.TenantId,
            ownerContext.OperatorId,
            useCase,
            cancellationToken);
        if (!provisionResult.Success || provisionResult.Data is null)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(provisionResult.Code, provisionResult.Message);
        }

        var call = RemoteCallResult<HireTemplateResultDto>.Ok(new HireTemplateResultDto(
            provisionResult.Data.HireId,
            provisionResult.Data.SandboxId,
            provisionResult.Data.State,
            "start_conversation"));

        hireOwners[provisionResult.Data.HireId] = ownerContext with
        {
            TemplateId = EvaluationWorkspaceTemplateId,
            TemplateName = EvaluationWorkspaceTemplateName,
            EmployeeId = null
        };

        if (hiringRuntimeStore.Get(provisionResult.Data.HireId) is null)
        {
            var discoverySkill = BuildEvaluationWorkspaceDiscoverySkill();
            var stageCompletion = stageCompletionEvaluator.Evaluate(
                discoverySkill.StageRules,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
            var templatePackage = BuildEvaluationWorkspaceTemplatePackage();

            hiringRuntimeStore.Upsert(new HiringRuntimeContext
            {
                HireId = provisionResult.Data.HireId,
                TemplateId = EvaluationWorkspaceTemplateId,
                TemplateName = EvaluationWorkspaceTemplateName,
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                SandboxId = provisionResult.Data.SandboxId,
                CurrentStage = "evaluation",
                CollectionPhase = HiringCollectionPhase.NotStarted,
                IsConversationPaused = false,
                IsConversationResponding = false,
                TemplatePackage = templatePackage,
                DiscoverySkill = discoverySkill,
                StructuredData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                Materials = [],
                StageCompletion = stageCompletion
            });
        }

        logger.LogInformation(
            "Created evaluation workspace. TargetHireId={TargetHireId}, EvalHireId={EvalHireId}, EvalSandboxId={EvalSandboxId}",
            normalizedTargetHireId,
            provisionResult.Data.HireId,
            provisionResult.Data.SandboxId);

        return ApiResponse<HireTemplateResultDto>.SuccessResponse(call.Data, "evaluation workspace created");
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
                OwnerSubject = ownerContext.OwnerSubject
            },
            cancellationToken);
        if (!refreshResult.Success || refreshResult.Data is null)
        {
            return ApiResponse<HiringStatusDto>.ErrorResponse(refreshResult.Code, refreshResult.Message);
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
        var sessionResult = await sandboxService.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = normalizedHireId,
                SandboxRole = ResolveSandboxRole(normalizedHireId),
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                SandboxId = hiringRuntimeStore.Get(normalizedHireId)?.SandboxId,
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
            var currentStage = ResolveCurrentStage(runtimeContext.StageCompletion, call.Data.CurrentStage);
            var collectionPhase = ResolveCollectionPhase(
                runtimeContext.StageCompletion,
                runtimeContext.StructuredData,
                runtimeContext.CollectionPhase);
            runtimeContext = runtimeContext with
            {
                SessionId = call.Data.SessionId,
                CurrentStage = currentStage,
                CollectionPhase = collectionPhase
            };
            hiringRuntimeStore.Upsert(runtimeContext);
            call = RemoteCallResult<StartHiringConversationResultDto>.Ok(call.Data with
            {
                CurrentStage = currentStage,
                StageSkills = BuildStageSkills(runtimeContext.DiscoverySkill),
                IsConversationPaused = runtimeContext.IsConversationPaused,
                IsConversationResponding = IsConversationResponding(normalizedHireId, runtimeContext)
            });
        }

        await EnsureAssistantKickoffAsync(normalizedHireId, cancellationToken);

        return ApiResponse<StartHiringConversationResultDto>.SuccessResponse(call.Data);
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
             (request.StructuredAnswers is null || request.StructuredAnswers.Count == 0) &&
             (request.Materials is null || request.Materials.Count == 0)))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, "content 与 structuredAnswers 不能同时为空");
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
                hiringRuntimeStore.Upsert(runtimeContext with { IsConversationResponding = true });
            }

            var ownerContext = ResolveOwnerContextByHireId(normalizedHireId);
            var sendResponse = await sandboxService.SendMessageAsync(
                new SandboxSendMessageRequestDto
                {
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = normalizedHireId,
                    SandboxRole = ResolveSandboxRole(normalizedHireId),
                    OwnerSubject = ownerContext.OwnerSubject,
                    TenantId = ownerContext.TenantId,
                    OperatorId = ownerContext.OperatorId,
                    SessionKey = "default",
                    SandboxId = runtimeContext?.SandboxId,
                    Content = request.Content,
                    StructuredAnswers = request.StructuredAnswers,
                    Materials = request.Materials
                },
                cancellationToken);

            if (!sendResponse.Success || sendResponse.Data is null)
            {
                return ApiResponse<HiringConversationResultDto>.ErrorResponse(sendResponse.Code, sendResponse.Message);
            }

            var call = RemoteCallResult<HiringConversationResultDto>.Ok(sendResponse.Data);

            runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
            if (runtimeContext is not null)
            {
                var structuredData = MergeStructuredData(runtimeContext.StructuredData, request.StructuredAnswers);
                var materials = MergeMaterials(runtimeContext.Materials, BuildMaterialsFromRequest(request));
                var stageCompletion = stageCompletionEvaluator.Evaluate(runtimeContext.DiscoverySkill.StageRules, structuredData);
                var currentStage = ResolveCurrentStage(stageCompletion, runtimeContext.CurrentStage);
                var collectionPhase = ResolveCollectionPhase(stageCompletion, structuredData, HiringCollectionPhase.InProgress);
                var preview = BuildLocalStagePreview(
                    normalizedHireId,
                    runtimeContext.DiscoverySkill,
                    stageCompletion,
                    currentStage,
                    collectionPhase,
                    structuredData,
                    call.Data.AssistantMessage.Content);

                runtimeContext = runtimeContext with
                {
                    SessionId = call.Data.SessionId,
                    CurrentStage = currentStage,
                    CollectionPhase = collectionPhase,
                    StructuredData = structuredData,
                    Materials = materials,
                    StageCompletion = stageCompletion,
                    IsConversationResponding = true
                };
                runtimeContext = ApplyConversationProgressToTemplatePackage(runtimeContext);
                if (ShouldPersistArtifactPackages(runtimeContext))
                {
                    await PersistIntermediatePackageAsync(runtimeContext, cancellationToken);
                }
                hiringRuntimeStore.Upsert(runtimeContext);

                call = RemoteCallResult<HiringConversationResultDto>.Ok(call.Data with
                {
                    CurrentStage = currentStage,
                    LatestPreview = preview,
                    IsConversationPaused = runtimeContext.IsConversationPaused,
                    IsConversationResponding = true
                });
            }

            return ApiResponse<HiringConversationResultDto>.SuccessResponse(call.Data);
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

        var ownerContext = ResolveOwnerContextByHireId(normalizedHireId);
        var timelineResponse = await sandboxService.GetTimelineAsync(
            new SandboxTimelineRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = normalizedHireId,
                SandboxRole = ResolveSandboxRole(normalizedHireId),
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                SessionKey = "default",
                SandboxId = hiringRuntimeStore.Get(normalizedHireId)?.SandboxId
            },
            cancellationToken);

        if (!timelineResponse.Success || timelineResponse.Data is null)
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(timelineResponse.Code, timelineResponse.Message);
        }

        var call = RemoteCallResult<HiringConversationTimelineDto>.Ok(timelineResponse.Data);

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is not null)
        {
            call = RemoteCallResult<HiringConversationTimelineDto>.Ok(call.Data with
            {
                CurrentStage = runtimeContext.CurrentStage,
                CollectionPhase = runtimeContext.CollectionPhase,
                StageSkills = BuildStageSkills(runtimeContext.DiscoverySkill)
            });
        }

        return ApiResponse<HiringConversationTimelineDto>.SuccessResponse(call.Data);
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
            : stage.Trim().ToUpperInvariant();
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

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        var logs = runtimeContext?.AuditLogs ?? [];
        return ApiResponse<IReadOnlyList<HiringAuditLogDto>>.SuccessResponse(logs);
    }

    public async Task<ApiResponse<HiringFinalizeResultDto>> FinalizeAsync(
        string hireId,
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

        var incompleteStages = runtimeContext.StageCompletion
            .Where(item => !item.ReadyForNextStage)
            .ToArray();
        if (incompleteStages.Length > 0)
        {
            var blockingFields = incompleteStages
                .SelectMany(item => item.BlockingFields.Select(field => $"{item.Stage}:{field}"))
                .ToArray();
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(
                409,
                $"仍有 discovery 阻塞字段未补齐：{string.Join("、", blockingFields)}");
        }

        var call = await SendForJsonAsync<HiringFinalizeResultDto>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/finalize",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var finalizeResult = call.Data;
        if (hireOwners.TryGetValue(normalizedHireId, out var ownerContext))
        {
            if (string.IsNullOrWhiteSpace(ownerContext.EmployeeId))
            {
                var capabilities = (await templateDataProvider.GetByIdAsync(ownerContext.TemplateId, cancellationToken))?.CoreAbilities ?? [];
                using var scope = serviceScopeFactory.CreateScope();
                var employeeRuntimeService = scope.ServiceProvider.GetRequiredService<IEmployeeRuntimeService>();
                var createResponse = await employeeRuntimeService.CreateFromHireAsync(
                    new CreateEmployeeFromHireRequestDto(
                        HireId: normalizedHireId,
                        TemplateId: ownerContext.TemplateId,
                        TemplateName: ownerContext.TemplateName,
                        OwnerSubject: ownerContext.OwnerSubject,
                        TenantId: ownerContext.TenantId,
                        OperatorId: ownerContext.OperatorId,
                        Capabilities: capabilities),
                    cancellationToken);

                if (createResponse.Success && createResponse.Data is not null)
                {
                    ownerContext = ownerContext with { EmployeeId = createResponse.Data.EmployeeId };
                    hireOwners[normalizedHireId] = ownerContext;
                }
            }

            if (!string.IsNullOrWhiteSpace(ownerContext.EmployeeId))
            {
                finalizeResult = finalizeResult with { EmployeeId = ownerContext.EmployeeId };
            }
        }

        var artifactArchiveCall = await SendForBytesAsync(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/artifacts/download",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);
        if (!artifactArchiveCall.Success || artifactArchiveCall.Data is null || artifactArchiveCall.Data.Length == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(artifactArchiveCall.StatusCode, artifactArchiveCall.Message);
        }

        var extractedArtifacts = ExtractZipEntries(artifactArchiveCall.Data);
        if (extractedArtifacts.Count == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(502, "后端交付包为空或无法解析");
        }

        var mergedArtifacts = MergeTemplatePackageArtifacts(extractedArtifacts, runtimeContext.TemplatePackage);
        if (mergedArtifacts.Count == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(502, "artifact archive merge produced no files");
        }

        var mergedArtifactArchive = BuildArtifactArchive(mergedArtifacts);
        if (!string.IsNullOrWhiteSpace(finalizeResult.EmployeeId))
        {
            try
            {
                var storedArtifacts = await instanceArtifactCloneService.StoreDepartmentArtifactsAsync(
                    finalizeResult.EmployeeId,
                    mergedArtifacts,
                    cancellationToken);
                var instance = await dbContext.Instances.FirstOrDefaultAsync(
                    item => item.InstanceId == finalizeResult.EmployeeId,
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
                logger.LogWarning(ex, "Failed to persist finalized instance artifacts. EmployeeId={EmployeeId}", finalizeResult.EmployeeId);
            }
        }

        if (ShouldPersistArtifactPackages(runtimeContext))
        {
            await artifactPackageService.PersistFinalPackageAsync(
                new HiringArtifactPackagePersistRequestDto(
                    runtimeContext.HireId,
                    runtimeContext.SessionId,
                    BuildFinalPackageFileName(normalizedHireId, artifactArchiveCall.FileName),
                    mergedArtifacts),
                cancellationToken);
        }

        runtimeContext = runtimeContext with
        {
            CurrentStage = finalizeResult.CurrentStage,
            CollectionPhase = finalizeResult.CollectionPhase,
            EmployeeId = finalizeResult.EmployeeId,
            ArtifactFiles = mergedArtifacts,
            ArtifactArchive = mergedArtifactArchive,
            ArtifactArchiveFileName = artifactArchiveCall.FileName
        };
        hiringRuntimeStore.Upsert(runtimeContext);

        finalizeResult = finalizeResult with
        {
            GeneratedFiles = mergedArtifacts.Keys.ToArray(),
            DownloadUrl = $"/api/v1/hirings/{normalizedHireId}/artifacts/download"
        };

        return ApiResponse<HiringFinalizeResultDto>.SuccessResponse(finalizeResult, "交付物已生成");
    }

    public async Task<ApiResponse<HiringWorkflowStateDto>> GetWorkflowStateAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringWorkflowStateDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringWorkflowStateDto>.ErrorResponse(404, "雇佣上下文不存在，请重新发起流程");
        }

        var workflowState = new HiringWorkflowStateDto(
            HireId: normalizedHireId,
            SessionId: runtimeContext.SessionId,
            CurrentStage: runtimeContext.CurrentStage,
            RequiresAudit: false,
            CollectionPhase: runtimeContext.CollectionPhase,
            StageSkills: BuildStageSkills(runtimeContext.DiscoverySkill),
            AuditLogs: runtimeContext.AuditLogs,
            TemplatePackageId: runtimeContext.TemplatePackage.PackageId,
            TemplatePackageVersion: runtimeContext.TemplatePackage.PackageVersion,
            DiscoverySkillId: runtimeContext.DiscoverySkill.SkillId,
            DiscoverySkillVersion: runtimeContext.DiscoverySkill.SkillVersion,
            StageCompletion: runtimeContext.StageCompletion,
            IsConversationPaused: runtimeContext.IsConversationPaused,
            IsConversationResponding: IsConversationResponding(normalizedHireId, runtimeContext));

        return ApiResponse<HiringWorkflowStateDto>.SuccessResponse(workflowState);
    }

    public async Task<ApiResponse<bool>> UploadEvaluationSkillAsync(
        string hireId,
        string? skillRootPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<bool>.ErrorResponse(400, error);
        }

        var payloadResult = await BuildEvaluationSkillUploadPayloadAsync(skillRootPath, cancellationToken);
        if (!payloadResult.Success || payloadResult.Data is null)
        {
            return ApiResponse<bool>.ErrorResponse(payloadResult.Code, payloadResult.Message);
        }

        var uploadCall = await UploadSystemSkillPackageAsync(
            normalizedHireId,
            ResolveOwnerByHireId(normalizedHireId),
            payloadResult.Data,
            cancellationToken);

        if (!uploadCall.Success || uploadCall.Data is null)
        {
            return ApiResponse<bool>.ErrorResponse(uploadCall.StatusCode, uploadCall.Message);
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is not null)
        {
            var evaluationSkill = BuildDiscoverySkillFromUploadPayload(payloadResult.Data);
            var stageCompletion = stageCompletionEvaluator.Evaluate(evaluationSkill.StageRules, runtimeContext.StructuredData);
            var currentStage = ResolveCurrentStage(stageCompletion, runtimeContext.CurrentStage);
            var collectionPhase = ResolveCollectionPhase(stageCompletion, runtimeContext.StructuredData, runtimeContext.CollectionPhase);

            runtimeContext = runtimeContext with
            {
                DiscoverySkill = evaluationSkill,
                StageCompletion = stageCompletion,
                CurrentStage = currentStage,
                CollectionPhase = collectionPhase
            };
            hiringRuntimeStore.Upsert(runtimeContext);
        }

        logger.LogInformation(
            "Uploaded evaluation skill package. EvalHireId={EvalHireId}, SkillId={SkillId}, SkillVersion={SkillVersion}",
            normalizedHireId,
            uploadCall.Data.SkillId,
            uploadCall.Data.SkillVersion);

        return ApiResponse<bool>.SuccessResponse(true, "evaluation skill uploaded");
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

        await Task.CompletedTask;
        var structuredData = NormalizeStructuredData(runtimeContext.StructuredData);
        var stageCompletion = stageCompletionEvaluator.Evaluate(runtimeContext.DiscoverySkill.StageRules, structuredData);
        var currentStage = ResolveCurrentStage(stageCompletion, runtimeContext.CurrentStage);
        var collectionPhase = ResolveCollectionPhase(stageCompletion, structuredData, runtimeContext.CollectionPhase);

        runtimeContext = runtimeContext with
        {
            CurrentStage = currentStage,
            CollectionPhase = collectionPhase,
            StructuredData = structuredData,
            StageCompletion = stageCompletion
        };
        hiringRuntimeStore.Upsert(runtimeContext);

        return runtimeContext;
    }

    private static IReadOnlyList<StageSkillMappingDto> BuildStageSkills(DiscoverySkillDefinition discoverySkill)
    {
        return discoverySkill.StageRules
            .Select(rule => new StageSkillMappingDto(
                Stage: rule.Stage,
                SkillName: rule.SkillName,
                RequiredFields: rule.RequiredFields,
                Description: rule.Description))
            .ToArray();
    }

    private static HiringStagePreviewDto EnrichStagePreview(
        HiringStagePreviewDto preview,
        DiscoverySkillDefinition discoverySkill,
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        string currentStage,
        string collectionPhase,
        IReadOnlyDictionary<string, string?> structuredData)
    {
        var currentRule = discoverySkill.StageRules.FirstOrDefault(rule =>
            string.Equals(rule.Stage, currentStage, StringComparison.OrdinalIgnoreCase));
        var currentCompletion = stageCompletion.FirstOrDefault(item =>
            string.Equals(item.Stage, currentStage, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<string> riskNotes;
        if (string.Equals(collectionPhase, HiringCollectionPhase.ReadyForFinalize, StringComparison.OrdinalIgnoreCase))
        {
            riskNotes = ["所有 discovery 阶段已满足，可执行 finalize 生成实例交付物。"];
        }
        else if (currentCompletion is not null && currentCompletion.BlockingFields.Count > 0)
        {
            riskNotes = [$"当前阶段仍缺少字段：{string.Join("、", currentCompletion.BlockingFields)}"];
        }
        else
        {
            riskNotes = ["当前阶段字段已齐全，可进入下一阶段。"];
        }

        return preview with
        {
            Stage = currentStage,
            SkillName = currentRule?.SkillName ?? preview.SkillName,
            StructuredData = structuredData,
            MissingFields = currentCompletion?.BlockingFields ?? preview.MissingFields,
            RiskNotes = riskNotes,
            ReadyForAudit = currentCompletion?.ReadyForNextStage ?? preview.ReadyForAudit
        };
    }

    private Task<RemoteCallResult<SystemSkillUploadResult>> UploadDiscoverySystemSkillAsync(
        string hireId,
        DiscoverySkillDefinition discoverySkill,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        return UploadSystemSkillPackageAsync(
            hireId,
            ownerSubject,
            BuildSystemSkillUploadPayload(discoverySkill),
            cancellationToken);
    }

    private Task<RemoteCallResult<TemplatePackageUploadResult>> UploadTemplatePackageAsync(
        string hireId,
        TemplatePackageDefinition templatePackage,
        DiscoverySkillDefinition discoverySkill,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        return UploadTemplatePackageViaDigitalEmployeeAsync(
            hireId,
            templatePackage,
            discoverySkill,
            ownerSubject,
            cancellationToken);
    }

    private async Task<RemoteCallResult<TemplatePackageUploadResult>> UploadTemplatePackageViaDigitalEmployeeAsync(
        string hireId,
        TemplatePackageDefinition templatePackage,
        DiscoverySkillDefinition discoverySkill,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var archiveBytes = BuildDigitalEmployeeArchive(templatePackage, discoverySkill);
        var fileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";
        var uploadCall = await UploadSandboxArchiveAsync(
            hireId,
            ownerSubject,
            archiveBytes,
            fileName,
            cancellationToken);
        if (!uploadCall.Success || uploadCall.Data is null)
        {
            return RemoteCallResult<TemplatePackageUploadResult>.Failure(uploadCall.StatusCode, uploadCall.Message);
        }

        if (!uploadCall.Data.Success)
        {
            return RemoteCallResult<TemplatePackageUploadResult>.Failure(
                502,
                string.IsNullOrWhiteSpace(uploadCall.Data.Error) ? "数字员工模板包上传失败" : uploadCall.Data.Error);
        }

        return RemoteCallResult<TemplatePackageUploadResult>.Ok(new TemplatePackageUploadResult(
            HireId: hireId,
            SandboxId: string.Empty,
            PackageId: templatePackage.PackageId,
            PackageVersion: templatePackage.PackageVersion,
            PackageHash: templatePackage.PackageHash,
            InstalledPath: "workspace"));
    }

    private static SystemSkillUploadPayload BuildSystemSkillUploadPayload(DiscoverySkillDefinition discoverySkill)
    {
        return new SystemSkillUploadPayload(
            SkillId: discoverySkill.SkillId,
            SkillVersion: discoverySkill.SkillVersion,
            SkillHash: discoverySkill.SkillHash,
            Files: discoverySkill.Files
                .Select(file => new SystemSkillFileUploadPayload(
                    RelativePath: file.RelativePath,
                    ContentHash: file.ContentHash,
                    Content: file.Content))
                .ToArray(),
            StageRules: discoverySkill.StageRules
                .Select(rule => new SystemSkillStageRuleUploadPayload(
                    Stage: rule.Stage,
                    SkillName: rule.SkillName,
                    Description: rule.Description,
                    RequiredFields: rule.RequiredFields))
                .ToArray());
    }

    private async Task<ApiResponse<SystemSkillUploadPayload>> BuildEvaluationSkillUploadPayloadAsync(
        string? skillRootPath,
        CancellationToken cancellationToken)
    {
        SystemSkillPackage package;
        try
        {
            package = await systemSkillRegistry.LoadRequiredAsync(
                EvaluationSkillId,
                skillRootPath,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<SystemSkillUploadPayload>.ErrorResponse(422, ex.Message);
        }

        if (package.StageRules.Count == 0)
        {
            return ApiResponse<SystemSkillUploadPayload>.ErrorResponse(422, "evaluation system skill must declare stage rules");
        }

        if (package.Files.Count == 0)
        {
            return ApiResponse<SystemSkillUploadPayload>.ErrorResponse(422, "evaluation skill payload is empty");
        }

        var orderedFiles = package.Files
            .Select(file => new SystemSkillFileUploadPayload(
                RelativePath: file.RelativePath,
                ContentHash: file.ContentHash,
                Content: file.Content))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var payload = new SystemSkillUploadPayload(
            SkillId: package.SkillId,
            SkillVersion: package.Version,
            SkillHash: package.SkillHash,
            Files: orderedFiles,
            StageRules: package.StageRules
                .Select(rule => new SystemSkillStageRuleUploadPayload(
                    Stage: rule.Stage,
                    SkillName: rule.SkillName,
                    Description: rule.Description,
                    RequiredFields: rule.RequiredFields))
                .ToArray());

        return ApiResponse<SystemSkillUploadPayload>.SuccessResponse(payload);
    }

    private static DiscoverySkillDefinition BuildDiscoverySkillFromUploadPayload(SystemSkillUploadPayload payload)
    {
        var files = payload.Files
            .Select(file => new DiscoverySkillFileAsset(
                RelativePath: file.RelativePath,
                Content: file.Content,
                ContentHash: file.ContentHash))
            .ToArray();
        var stageRules = payload.StageRules
            .Select(rule => new DiscoveryStageRule(
                Stage: rule.Stage,
                SkillName: rule.SkillName,
                Description: rule.Description,
                RequiredFields: rule.RequiredFields))
            .ToArray();
        var rootContent = files
            .FirstOrDefault(file => file.RelativePath.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
            ?.Content
            ?? $"# {payload.SkillId}";

        return new DiscoverySkillDefinition(
            SkillId: payload.SkillId,
            SkillVersion: payload.SkillVersion,
            SkillHash: payload.SkillHash,
            SkillRootPath: payload.SkillId,
            SkillContent: rootContent,
            Files: files,
            StageRules: stageRules);
    }

    private static TemplatePackageDefinition BuildEvaluationWorkspaceTemplatePackage()
    {
        const string manifestJson = """
{
  "template_id": "evaluation-expert",
  "display_name": "Evaluation Expert Workspace",
  "description": "Workspace package for evaluator sandbox"
}
""";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);

        return new TemplatePackageDefinition(
            RequestedTemplateId: EvaluationWorkspaceTemplateId,
            PackageId: EvaluationWorkspaceTemplateId,
            PackageVersion: EvaluationSkillVersion,
            PackageHash: ComputeContentHash(manifestJson),
            SourceArchive: null,
            PackageRootPath: "evaluation-workspace",
            ManifestJson: manifestJson,
            DisplayName: EvaluationWorkspaceTemplateName,
            Description: "Evaluator sandbox template package",
            PackageFiles:
            [
                new TemplatePackageFileAsset(
                    RelativePath: "manifest.json",
                    Content: manifestBytes,
                    ContentHash: ComputeContentHash(manifestJson))
            ],
            OntologySlices: [],
            RequiredSkills: []);
    }

    private static DiscoverySkillDefinition BuildEvaluationWorkspaceDiscoverySkill()
    {
        const string rootSkillContent = """
# evaluation-expert

This is the bootstrap skill for evaluation sandbox orchestration.
""";
        var stageRule = new DiscoveryStageRule(
            Stage: "evaluation",
            SkillName: "evaluation_orchestrator",
            Description: "Evaluate target sandbox and output PASS or FAIL.",
            RequiredFields: ["evaluation_goal"]);

        return new DiscoverySkillDefinition(
            SkillId: EvaluationSkillId,
            SkillVersion: EvaluationSkillVersion,
            SkillHash: ComputeContentHash(rootSkillContent),
            SkillRootPath: "evaluation-expert",
            SkillContent: rootSkillContent,
            Files:
            [
                new DiscoverySkillFileAsset(
                    RelativePath: "SKILL.md",
                    Content: rootSkillContent,
                    ContentHash: ComputeContentHash(rootSkillContent))
            ],
            StageRules: [stageRule]);
    }

    /// <summary>
    /// 为雇佣流程创建托管沙箱，并同步等待沙箱就绪（最多 180 秒）。
    /// 此方法会阻塞直到沙箱状态变为 "Running" 且 GatewayEndpoint 可用。
    /// </summary>
    /// <param name="sandboxRole">沙箱角色，如 "hiring" 或 "evaluation-evaluator"</param>
    /// <param name="ownerSubject">沙箱所有者标识，格式为 "tenant:operator" 或 JWT sub claim</param>
    /// <param name="tenantId">租户 ID</param>
    /// <param name="operatorId">操作员 ID</param>
    /// <param name="useCase">用例描述，用于审计和追踪</param>
    /// <returns>包含 hireId、sandboxId、状态和网关地址的绑定信息</returns>
    private async Task<ApiResponse<ProvisionedSandboxBinding>> ProvisionManagedHireSandboxAsync(
        string sandboxRole,
        string ownerSubject,
        string tenantId,
        string operatorId,
        string? useCase,
        CancellationToken cancellationToken)
    {
        var hireId = $"hire-{Guid.NewGuid():N}";
        var createResult = await sandboxService.CreateAsync(
            new SandboxCreateRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = sandboxRole,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                ProvisioningMode = "managed",
                UseCase = useCase
            },
            cancellationToken);
        if (!createResult.Success || createResult.Data is null)
        {
            return ApiResponse<ProvisionedSandboxBinding>.ErrorResponse(createResult.Code, createResult.Message);
        }

        var readyResult = await WaitForManagedSandboxReadyAsync(createResult.Data, cancellationToken);
        if (!readyResult.Success || readyResult.Data is null)
        {
            return ApiResponse<ProvisionedSandboxBinding>.ErrorResponse(readyResult.Code, readyResult.Message);
        }

        return ApiResponse<ProvisionedSandboxBinding>.SuccessResponse(
            new ProvisionedSandboxBinding(
                hireId,
                readyResult.Data.SandboxId,
                readyResult.Data.State,
                readyResult.Data.GatewayEndpoint));
    }

    /// <summary>
    /// 轮询等待托管沙箱就绪（状态为 "Running" 且 GatewayEndpoint 非空）。
    /// 最多轮询 36 次，每次间隔 5 秒，总计最多等待 180 秒。
    /// </summary>
    /// <param name="instance">沙箱实例初始状态</param>
    /// <returns>就绪后的沙箱实例信息，或超时错误</returns>
    private async Task<ApiResponse<SandboxInstanceDto>> WaitForManagedSandboxReadyAsync(
        SandboxInstanceDto instance,
        CancellationToken cancellationToken)
    {
        if (string.Equals(instance.State, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(instance.GatewayEndpoint))
        {
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(instance);
        }

        for (var attempt = 0; attempt < 36; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var refreshResult = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = instance.SandboxId
                },
                cancellationToken);
            if (!refreshResult.Success || refreshResult.Data is null)
            {
                return ApiResponse<SandboxInstanceDto>.ErrorResponse(refreshResult.Code, refreshResult.Message);
            }

            if (string.Equals(refreshResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint))
            {
                return ApiResponse<SandboxInstanceDto>.SuccessResponse(refreshResult.Data);
            }
        }

        return ApiResponse<SandboxInstanceDto>.ErrorResponse(504, "sandbox 启动超时，网关 endpoint 尚未就绪");
    }

    private async Task<RemoteCallResult<DigitalEmployeeUploadResponse>> UploadSandboxArchiveAsync(
        string hireId,
        string ownerSubject,
        byte[] archiveBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        var gatewayTargetResult = await ResolveSandboxGatewayTargetAsync(hireId, ownerSubject, cancellationToken);
        if (!gatewayTargetResult.Success || gatewayTargetResult.Data is null)
        {
            return RemoteCallResult<DigitalEmployeeUploadResponse>.Failure(gatewayTargetResult.Code, gatewayTargetResult.Message);
        }

        var call = await kingCrabHttpClient.SendMultipartForJsonAsync<DigitalEmployeeUploadResponse>(
            gatewayTargetResult.Data.GatewayEndpoint,
            "file",
            fileName,
            archiveBytes,
            "application/zip",
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        return call.Success && call.Data is not null
            ? RemoteCallResult<DigitalEmployeeUploadResponse>.Ok(call.Data)
            : RemoteCallResult<DigitalEmployeeUploadResponse>.Failure(call.StatusCode, call.Message);
    }

    private async Task<RemoteCallResult<SystemSkillUploadResult>> UploadSystemSkillPackageAsync(
        string hireId,
        string ownerSubject,
        SystemSkillUploadPayload payload,
        CancellationToken cancellationToken)
    {
        var archiveBytes = BuildSystemSkillArchive(payload);
        var uploadCall = await UploadSandboxArchiveAsync(
            hireId,
            ownerSubject,
            archiveBytes,
            $"{payload.SkillId}-{payload.SkillVersion}.zip",
            cancellationToken);
        if (!uploadCall.Success || uploadCall.Data is null)
        {
            return RemoteCallResult<SystemSkillUploadResult>.Failure(uploadCall.StatusCode, uploadCall.Message);
        }

        if (!uploadCall.Data.Success)
        {
            return RemoteCallResult<SystemSkillUploadResult>.Failure(
                502,
                string.IsNullOrWhiteSpace(uploadCall.Data.Error) ? "system skill 上传失败" : uploadCall.Data.Error);
        }

        return RemoteCallResult<SystemSkillUploadResult>.Ok(new SystemSkillUploadResult(
            HireId: hireId,
            SandboxId: string.Empty,
            SkillId: payload.SkillId,
            SkillVersion: payload.SkillVersion,
            SkillHash: payload.SkillHash,
            InstalledPath: "workspace/skills",
            LoadedStageSkills: payload.StageRules
                .Select(rule => new StageSkillMappingDto(
                    rule.Stage,
                    rule.SkillName,
                    rule.RequiredFields,
                    rule.Description))
                .ToArray()));
    }

    private async Task<ApiResponse<SandboxGatewayTarget>> ResolveSandboxGatewayTargetAsync(
        string hireId,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var sandboxRole = ResolveSandboxRole(hireId);
        var refreshResult = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = sandboxRole,
                OwnerSubject = ownerSubject
            },
            cancellationToken);
        if (!refreshResult.Success || refreshResult.Data is null)
        {
            return ApiResponse<SandboxGatewayTarget>.ErrorResponse(refreshResult.Code, refreshResult.Message);
        }

        if (!string.Equals(refreshResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxGatewayTarget>.ErrorResponse(409, "sandbox 尚未就绪");
        }

        var uploadEndpointResult = await ResolveSandboxUploadEndpointAsync(
            refreshResult.Data.SandboxId,
            cancellationToken);
        if (!uploadEndpointResult.Success || string.IsNullOrWhiteSpace(uploadEndpointResult.Data))
        {
            return ApiResponse<SandboxGatewayTarget>.ErrorResponse(uploadEndpointResult.Code, uploadEndpointResult.Message);
        }

        if (!TryBuildSandboxUploadUrl(uploadEndpointResult.Data, out var uploadUrl))
        {
            return ApiResponse<SandboxGatewayTarget>.ErrorResponse(502, "sandbox gateway endpoint 格式无效");
        }

        return ApiResponse<SandboxGatewayTarget>.SuccessResponse(
            new SandboxGatewayTarget(
                refreshResult.Data.SandboxId,
                uploadUrl));
    }

    private async Task<ApiResponse<string>> ResolveSandboxUploadEndpointAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            return ApiResponse<string>.ErrorResponse(400, "sandboxId 不能为空");
        }

        var settings = SandboxProvisioningSettings.FromConfiguration(configuration);
        var connection = settings.BuildConnection();
        var lookupUrl = OpenSandboxProvisioner.BuildEndpointLookupUrl(
            connection.GetBaseUrl().TrimEnd('/'),
            sandboxId.Trim(),
            settings.GatewayPort,
            useServerProxy: false);

        using var response = await connection.GetHttpClient().GetAsync(lookupUrl, cancellationToken);
        var payload = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResponse<string>.ErrorResponse(
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(payload)
                    ? $"sandbox upload endpoint 查询失败（HTTP {(int)response.StatusCode}）"
                    : payload);
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("endpoint", out var endpointElement) ||
            string.IsNullOrWhiteSpace(endpointElement.GetString()))
        {
            return ApiResponse<string>.ErrorResponse(502, "sandbox upload endpoint 返回为空");
        }

        return ApiResponse<string>.SuccessResponse(endpointElement.GetString()!);
    }

    internal static bool TryBuildSandboxUploadUrl(string gatewayEndpoint, out string uploadUrl)
    {
        uploadUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(gatewayEndpoint))
        {
            return false;
        }

        var normalizedGatewayEndpoint = gatewayEndpoint.Trim();
        if (!normalizedGatewayEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalizedGatewayEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedGatewayEndpoint = $"http://{normalizedGatewayEndpoint.TrimStart('/')}";
        }

        if (!Uri.TryCreate(normalizedGatewayEndpoint, UriKind.Absolute, out var gatewayUri) ||
            !string.Equals(gatewayUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(gatewayUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (gatewayUri.AbsolutePath.EndsWith("/admin/digital-employee/upload", StringComparison.OrdinalIgnoreCase))
        {
            uploadUrl = gatewayUri.ToString();
            return true;
        }

        var normalizedBaseUrl = normalizedGatewayEndpoint.EndsWith("/", StringComparison.Ordinal)
            ? normalizedGatewayEndpoint
            : normalizedGatewayEndpoint + "/";

        uploadUrl = new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "admin/digital-employee/upload").ToString();
        return true;
    }

    private static byte[] BuildSystemSkillArchive(SystemSkillUploadPayload payload)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in payload.Files)
            {
                if (string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    continue;
                }

                var normalizedPath = "skills/" + payload.SkillId.Trim().Trim('/') + "/" + file.RelativePath.TrimStart('/', '\\').Replace('\\', '/');
                if (!TryNormalizeArchiveEntryPath(normalizedPath, out normalizedPath))
                {
                    continue;
                }

                var contentBytes = Encoding.UTF8.GetBytes(file.Content ?? string.Empty);
                var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(contentBytes, 0, contentBytes.Length);
            }
        }

        return memoryStream.ToArray();
    }

    private static HiringStagePreviewDto BuildLocalStagePreview(
        string hireId,
        DiscoverySkillDefinition discoverySkill,
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        string currentStage,
        string collectionPhase,
        IReadOnlyDictionary<string, string?> structuredData,
        string? summaryOverride)
    {
        var basePreview = new HiringStagePreviewDto(
            HireId: hireId,
            Stage: currentStage,
            SkillName: string.Empty,
            Summary: string.IsNullOrWhiteSpace(summaryOverride) ? $"当前阶段：{currentStage}" : summaryOverride.Trim(),
            StructuredData: structuredData,
            MissingFields: [],
            RiskNotes: [],
            ReadyForAudit: false,
            GeneratedAt: DateTimeOffset.UtcNow);

        return EnrichStagePreview(
            basePreview,
            discoverySkill,
            stageCompletion,
            currentStage,
            collectionPhase,
            structuredData);
    }

    internal static byte[] BuildDigitalEmployeeArchive(
        TemplatePackageDefinition templatePackage,
        DiscoverySkillDefinition discoverySkill)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in templatePackage.PackageFiles)
            {
                if (file.Content.Length == 0 || string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    continue;
                }

                if (!TryNormalizeArchiveEntryPath(file.RelativePath, out var normalizedPath))
                {
                    continue;
                }

                var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(file.Content, 0, file.Content.Length);
            }

            // Ensure system discovery skill is always present in uploaded package.
            foreach (var file in discoverySkill.Files)
            {
                if (string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    continue;
                }

                var normalizedPath = "skills/" + discoverySkill.SkillId.Trim().Trim('/') + "/" + file.RelativePath.TrimStart('/', '\\').Replace('\\', '/');
                if (!TryNormalizeArchiveEntryPath(normalizedPath, out normalizedPath))
                {
                    continue;
                }

                var contentBytes = Encoding.UTF8.GetBytes(file.Content ?? string.Empty);
                var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(contentBytes, 0, contentBytes.Length);
            }
        }

        return memoryStream.ToArray();
    }

    private static IReadOnlyList<HiringConversationMaterialDto> BuildMaterialsFromRequest(HiringConversationMessageRequestDto request)
    {
        var result = new List<HiringConversationMaterialDto>();
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var content = request.Content.Trim();
            result.Add(new HiringConversationMaterialDto
            {
                Type = "text",
                Name = $"conversation-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
                Content = content,
                ContentHash = ComputeContentHash(content),
                Size = Encoding.UTF8.GetByteCount(content),
                MimeType = "text/plain",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = "conversation"
                }
            });
        }

        if (request.Materials is not null)
        {
            foreach (var material in request.Materials)
            {
                var normalized = NormalizeMaterial(material);
                if (normalized is not null)
                {
                    result.Add(normalized);
                }
            }
        }

        return result;
    }

    private async Task EnsureAssistantKickoffAsync(string hireId, CancellationToken cancellationToken)
    {
        var ownerContext = ResolveOwnerContextByHireId(hireId);
        var timelineCall = await sandboxService.GetTimelineAsync(
            new SandboxTimelineRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = ResolveSandboxRole(hireId),
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                SessionKey = "default",
                SandboxId = hiringRuntimeStore.Get(hireId)?.SandboxId
            },
            cancellationToken);
        if (!timelineCall.Success || timelineCall.Data is null)
        {
            return;
        }

        var hasAssistantMessage = timelineCall.Data.Messages.Any(message =>
            string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase));
        if (hasAssistantMessage)
        {
            return;
        }

        var kickoffPrompt = configuration["HireBot:ConversationKickoffPrompt"];
        var request = new HiringConversationMessageRequestDto
        {
            Content = string.IsNullOrWhiteSpace(kickoffPrompt) ? DefaultConversationKickoffPrompt : kickoffPrompt.Trim()
        };
        await sandboxService.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = ResolveSandboxRole(hireId),
                OwnerSubject = ownerContext.OwnerSubject,
                TenantId = ownerContext.TenantId,
                OperatorId = ownerContext.OperatorId,
                SessionKey = "default",
                SandboxId = hiringRuntimeStore.Get(hireId)?.SandboxId,
                Content = request.Content
            },
            cancellationToken);
    }

    internal static HiringRuntimeContext ApplyConversationProgressToTemplatePackage(HiringRuntimeContext runtimeContext)
    {
        var enrichedFiles = runtimeContext.TemplatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);

        var structuredDataJson = JsonSerializer.Serialize(runtimeContext.StructuredData, JsonOptions);
        var materialsJson = JsonSerializer.Serialize(runtimeContext.Materials, JsonOptions);
        UpsertPackageFile(enrichedFiles, "ontology/hiring-session/structured-data.json", structuredDataJson);
        UpsertPackageFile(enrichedFiles, "ontology/hiring-session/materials.json", materialsJson);
        if (TryBuildEvaluationTestCases(runtimeContext, out var evaluationTestCasesJson))
        {
            UpsertPackageFile(enrichedFiles, "testcases/evaluation-test-cases.json", evaluationTestCasesJson);
            UpsertPackageFile(enrichedFiles, "ontology/hiring-session/evaluation-test-cases.json", evaluationTestCasesJson);
        }

        var enrichedTemplatePackage = runtimeContext.TemplatePackage with
        {
            PackageFiles = enrichedFiles.Values.ToArray()
        };

        return runtimeContext with
        {
            TemplatePackage = enrichedTemplatePackage
        };
    }

    internal static void UpsertPackageFile(
        IDictionary<string, TemplatePackageFileAsset> packageFiles,
        string relativePath,
        string content)
    {
        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        packageFiles[normalizedPath] = new TemplatePackageFileAsset(normalizedPath, bytes, hash);
    }

    private static bool TryBuildEvaluationTestCases(HiringRuntimeContext runtimeContext, out string testCasesJson)
    {
        testCasesJson = string.Empty;
        var evaluationSkillMaterials = runtimeContext.Materials
            .Where(material => string.Equals(material.Type, "skill", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (evaluationSkillMaterials.Length == 0)
        {
            return false;
        }

        var guidanceLines = new List<string>();
        foreach (var material in evaluationSkillMaterials)
        {
            if (material.Metadata?.TryGetValue("skillName", out var skillName) == true && !string.IsNullOrWhiteSpace(skillName))
            {
                guidanceLines.Add($"skillName: {skillName.Trim()}");
            }

            if (material.Metadata?.TryGetValue("description", out var description) == true && !string.IsNullOrWhiteSpace(description))
            {
                guidanceLines.Add($"description: {description.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(material.Content))
            {
                var skillArchiveGuidance = ExtractEvaluationGuidanceFromArchive(material);
                if (!string.IsNullOrWhiteSpace(skillArchiveGuidance))
                {
                    guidanceLines.Add(skillArchiveGuidance);
                }
                else
                {
                    guidanceLines.Add(material.Content.Trim());
                }
            }
        }

        var skillGuidance = string.Join('\n', guidanceLines).Trim();

        var businessGoal = ResolveStructuredValue(runtimeContext.StructuredData, "business_goal", "expected_outcome", "goal")
                           ?? runtimeContext.TemplateName;
        var userProfile = ResolveStructuredValue(runtimeContext.StructuredData, "user_profile", "owner")
                          ?? "业务团队";
        var scenario = ResolveStructuredValue(runtimeContext.StructuredData, "expected_outcome", "trigger_event")
                       ?? "关键业务流程";

        var testCases = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            source = "conversation-skill-guided",
            skillSummary = Truncate(skillGuidance, 1200),
            cases = new object[]
            {
                new
                {
                    caseId = "eval-case-001",
                    title = $"{businessGoal} - 正常流程闭环",
                    objective = "验证数字员工在标准输入下能够完整执行流程并形成闭环回复",
                    profile = userProfile,
                    scenario,
                    expectedChecks = new[]
                    {
                        "覆盖预期行为序列的关键步骤",
                        "输出包含明确结论和下一步动作",
                        "关键字段采集完整且无空值"
                    }
                },
                new
                {
                    caseId = "eval-case-002",
                    title = $"{businessGoal} - 异常路径处置",
                    objective = "验证数字员工在信息缺失或异常输入下能够回退并给出风险提示",
                    profile = userProfile,
                    scenario = "输入缺失关键字段或存在冲突信息",
                    expectedChecks = new[]
                    {
                        "识别阻塞字段并明确追问",
                        "不跳过关键校验步骤",
                        "给出可执行的处置方案"
                    }
                },
                new
                {
                    caseId = "eval-case-003",
                    title = $"{businessGoal} - 工具调用与合规",
                    objective = "验证数字员工工具调用时机、参数和流程合规性",
                    profile = userProfile,
                    scenario,
                    expectedChecks = new[]
                    {
                        "必须工具调用不缺失",
                        "工具参数与上下文一致",
                        "流程顺序和合规约束满足要求"
                    }
                }
            }
        };

        testCasesJson = JsonSerializer.Serialize(testCases, JsonOptions);
        return true;
    }

    private static string? ExtractEvaluationGuidanceFromArchive(HiringConversationMaterialDto material)
    {
        var storagePath = material.Metadata is not null && material.Metadata.TryGetValue("storagePath", out var storagePathValue)
            ? storagePathValue
            : null;

        var archiveFormat = material.Metadata is not null && material.Metadata.TryGetValue("archiveFormat", out var archiveFormatValue)
            ? archiveFormatValue
            : null;
        var isZip = material.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(archiveFormat, "zip", StringComparison.OrdinalIgnoreCase);
        if (!isZip)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(material.Content))
        {
            return ExtractEvaluationGuidanceFromStoredArchive(storagePath);
        }

        var base64Content = material.Content.Trim();
        var base64Index = base64Content.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (base64Index >= 0)
        {
            base64Content = base64Content[(base64Index + "base64,".Length)..];
        }

        byte[] archiveBytes;
        try
        {
            archiveBytes = Convert.FromBase64String(base64Content);
        }
        catch (FormatException)
        {
            return null;
        }

        var snippets = new List<string>();
        using var memoryStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!entry.FullName.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            snippets.Add($"[{entry.FullName}]");
            snippets.Add(Truncate(content, 2000));
        }

        if (snippets.Count == 0)
        {
            return null;
        }

        return string.Join('\n', snippets);
    }

    private static string? ExtractEvaluationGuidanceFromStoredArchive(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || !File.Exists(storagePath))
        {
            return null;
        }

        var snippets = new List<string>();
        using var stream = File.OpenRead(storagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!entry.FullName.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            snippets.Add($"[{entry.FullName}]");
            snippets.Add(Truncate(content, 2000));
        }

        return snippets.Count == 0 ? null : string.Join('\n', snippets);
    }

    private static string? ResolveStructuredValue(
        IReadOnlyDictionary<string, string?> structuredData,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (structuredData.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return $"{trimmed[..maxLength]}...";
    }

    private async Task PersistIntermediatePackageAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        await artifactPackageService.PersistIntermediatePackageAsync(
            new HiringArtifactPackagePersistRequestDto(
                runtimeContext.HireId,
                runtimeContext.SessionId,
                BuildIntermediatePackageFileName(runtimeContext.HireId),
                BuildPackageFileMap(runtimeContext.TemplatePackage)),
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, byte[]> BuildPackageFileMap(TemplatePackageDefinition templatePackage)
    {
        return templatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file.Content,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool ShouldPersistArtifactPackages(HiringRuntimeContext runtimeContext)
    {
        return !string.IsNullOrWhiteSpace(runtimeContext.SessionId) &&
               !string.Equals(
                   runtimeContext.TemplateId,
                   EvaluationWorkspaceTemplateId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildIntermediatePackageFileName(string hireId)
    {
        return $"{hireId.Trim()}_intermediate_package.zip";
    }

    private static string BuildFinalPackageFileName(string hireId, string? upstreamFileName)
    {
        return string.IsNullOrWhiteSpace(upstreamFileName)
            ? $"{hireId.Trim()}_final_package.zip"
            : upstreamFileName.Trim();
    }

    private static HiringConversationMaterialDto? NormalizeMaterial(HiringConversationMaterialDto? material)
    {
        if (material is null)
        {
            return null;
        }

        var type = string.IsNullOrWhiteSpace(material.Type) ? "file" : material.Type.Trim();
        var name = string.IsNullOrWhiteSpace(material.Name)
            ? $"{type}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"
            : material.Name.Trim();
        var content = string.IsNullOrWhiteSpace(material.Content) ? null : material.Content;
        return material with
        {
            Type = type,
            Name = name,
            Content = content,
            ContentHash = string.IsNullOrWhiteSpace(material.ContentHash) && content is not null
                ? ComputeContentHash(content)
                : material.ContentHash,
            Size = material.Size ?? (content is null ? null : Encoding.UTF8.GetByteCount(content)),
            MimeType = string.IsNullOrWhiteSpace(material.MimeType) ? null : material.MimeType.Trim(),
            Metadata = material.Metadata?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<HiringConversationMaterialDto> MergeMaterials(
        IReadOnlyList<HiringConversationMaterialDto> existing,
        IReadOnlyList<HiringConversationMaterialDto> incoming)
    {
        if (incoming.Count == 0)
        {
            return existing;
        }

        var result = existing.ToList();
        foreach (var material in incoming)
        {
            var hasDuplicate = !string.IsNullOrWhiteSpace(material.ContentHash) &&
                               result.Any(existingMaterial => string.Equals(
                                   existingMaterial.ContentHash,
                                   material.ContentHash,
                                   StringComparison.OrdinalIgnoreCase));
            if (!hasDuplicate)
            {
                result.Add(material);
            }
        }

        return result;
    }

    private static string ComputeContentHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static Dictionary<string, string?> NormalizeStructuredData(IReadOnlyDictionary<string, string?>? source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            result[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
        }

        return result;
    }

    private static Dictionary<string, string?> MergeStructuredData(
        IReadOnlyDictionary<string, string?> existing,
        IReadOnlyDictionary<string, string>? incoming)
    {
        var result = NormalizeStructuredData(existing);
        if (incoming is null)
        {
            return result;
        }

        foreach (var pair in incoming)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            result[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
        }

        return result;
    }

    private static string ResolveCurrentStage(
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        string fallbackStage)
    {
        var nextStage = stageCompletion.FirstOrDefault(item => !item.ReadyForNextStage);
        if (nextStage is not null)
        {
            return nextStage.Stage;
        }

        return string.Equals(fallbackStage, HiringCollectionStage.Done, StringComparison.OrdinalIgnoreCase)
            ? HiringCollectionStage.Done
            : HiringCollectionStage.Package;
    }

    private static string ResolveCollectionPhase(
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        IReadOnlyDictionary<string, string?> structuredData,
        string fallbackPhase)
    {
        if (string.Equals(fallbackPhase, HiringCollectionPhase.Finalized, StringComparison.OrdinalIgnoreCase))
        {
            return HiringCollectionPhase.Finalized;
        }

        if (structuredData.Count == 0)
        {
            return HiringCollectionPhase.NotStarted;
        }

        return stageCompletion.All(item => item.ReadyForNextStage)
            ? HiringCollectionPhase.ReadyForFinalize
            : HiringCollectionPhase.InProgress;
    }

    private async Task<RemoteCallResult<T>> SendForJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var call = await kingCrabHttpClient.SendForJsonAsync<T>(
            method,
            path,
            body,
            ownerSubject,
            cancellationToken);

        return call.Success && call.Data is not null
            ? RemoteCallResult<T>.Ok(call.Data)
            : RemoteCallResult<T>.Failure(call.StatusCode, call.Message);
    }

    private async Task<RemoteCallResult<T>> SendMultipartForJsonAsync<T>(
        string path,
        string formFieldName,
        string fileName,
        byte[] fileBytes,
        string contentType,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var call = await kingCrabHttpClient.SendMultipartForJsonAsync<T>(
            path,
            formFieldName,
            fileName,
            fileBytes,
            contentType,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        return call.Success && call.Data is not null
            ? RemoteCallResult<T>.Ok(call.Data)
            : RemoteCallResult<T>.Failure(call.StatusCode, call.Message);
    }

    private async Task<RemoteBinaryCallResult> SendForBytesAsync(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var call = await kingCrabHttpClient.SendForBinaryAsync(
            method,
            path,
            body,
            ownerSubject,
            cancellationToken);

        return call.Success && call.Data is not null
            ? RemoteBinaryCallResult.Ok(call.FileName ?? "hirebot_artifacts.zip", call.ContentType ?? "application/octet-stream", call.Data)
            : RemoteBinaryCallResult.Failure(call.StatusCode, call.Message);
    }

    private static IReadOnlyDictionary<string, byte[]> ExtractZipEntries(byte[] archiveBytes)
    {
        using var memoryStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: false);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!TryNormalizeArchiveEntryPath(entry.FullName, out var normalizedPath))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            result[normalizedPath] = buffer.ToArray();
        }

        return result;
    }

    private static IReadOnlyDictionary<string, byte[]> MergeTemplatePackageArtifacts(
        IReadOnlyDictionary<string, byte[]> generatedArtifacts,
        TemplatePackageDefinition templatePackage)
    {
        var mergedArtifacts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in generatedArtifacts)
        {
            if (!TryNormalizeArchiveEntryPath(pair.Key, out var normalizedPath) || pair.Value.Length == 0)
            {
                continue;
            }

            mergedArtifacts[normalizedPath] = pair.Value;
        }

        foreach (var packageFile in templatePackage.PackageFiles)
        {
            if (!TryNormalizeArchiveEntryPath(packageFile.RelativePath, out var normalizedPath) ||
                packageFile.Content.Length == 0)
            {
                continue;
            }

            mergedArtifacts.TryAdd(normalizedPath, packageFile.Content);
        }

        return mergedArtifacts;
    }

    private static byte[] BuildArtifactArchive(IReadOnlyDictionary<string, byte[]> files)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryNormalizeArchiveEntryPath(pair.Key, out var normalizedPath) || pair.Value.Length == 0)
                {
                    continue;
                }

                var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(pair.Value, 0, pair.Value.Length);
            }
        }

        return memoryStream.ToArray();
    }

    private static bool TryNormalizeArchiveEntryPath(string path, out string normalizedPath)
    {
        return TryNormalizeArtifactPath(path, out normalizedPath, out _);
    }

    private HireOwnerContext ResolveOwnerContextForEvaluation(string targetHireId)
    {
        if (TryResolveOwnerContext(targetHireId, out var ownerContext))
        {
            return ownerContext;
        }

        var ownerSubject = ResolveOwnerSubject();
        if (TryParseOwnerSubject(ownerSubject, out var parsedTenantId, out var parsedOperatorId))
        {
            return new HireOwnerContext(
                OwnerSubject: ownerSubject,
                TenantId: parsedTenantId,
                OperatorId: parsedOperatorId,
                TemplateId: EvaluationWorkspaceTemplateId,
                TemplateName: EvaluationWorkspaceTemplateName,
                EmployeeId: null);
        }

        var (tenantId, operatorId) = ResolveTenantAndOperator(null, null);
        return new HireOwnerContext(
            OwnerSubject: ownerSubject,
            TenantId: tenantId,
            OperatorId: operatorId,
            TemplateId: EvaluationWorkspaceTemplateId,
            TemplateName: EvaluationWorkspaceTemplateName,
            EmployeeId: null);
    }

    private bool TryResolveOwnerContext(string hireId, out HireOwnerContext ownerContext)
    {
        if (hireOwners.TryGetValue(hireId, out var cachedOwnerContext))
        {
            ownerContext = cachedOwnerContext;
            return true;
        }

        var runtimeContext = hiringRuntimeStore.Get(hireId);
        if (runtimeContext is null)
        {
            var persistedOwnerContext = ResolvePersistedOwnerContext(hireId);
            if (persistedOwnerContext is null)
            {
                ownerContext = default!;
                return false;
            }

            hireOwners[hireId] = persistedOwnerContext;
            ownerContext = persistedOwnerContext;
            return true;
        }

        ownerContext = new HireOwnerContext(
            OwnerSubject: runtimeContext.OwnerSubject,
            TenantId: runtimeContext.TenantId,
            OperatorId: runtimeContext.OperatorId,
            TemplateId: runtimeContext.TemplateId,
            TemplateName: runtimeContext.TemplateName,
            EmployeeId: runtimeContext.EmployeeId);
        return true;
    }

    private HireOwnerContext? ResolvePersistedOwnerContext(string hireId)
    {
        var sandboxInstance = dbContext.SandboxInstances
            .AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault(item =>
                item.ScopeType == SandboxScopeTypes.Hire &&
                item.ScopeKey == hireId &&
                item.State != "Deleted");
        if (sandboxInstance is null)
        {
            return null;
        }

        return new HireOwnerContext(
            OwnerSubject: sandboxInstance.OwnerSubject,
            TenantId: sandboxInstance.TenantId,
            OperatorId: sandboxInstance.OperatorId,
            TemplateId: string.Empty,
            TemplateName: string.Empty,
            EmployeeId: null);
    }

    private HireOwnerContext ResolveOwnerContextByHireId(string hireId)
    {
        if (TryResolveOwnerContext(hireId, out var ownerContext))
        {
            return ownerContext;
        }

        var ownerSubject = ResolveOwnerSubject();
        var (tenantId, operatorId) = ResolveTenantAndOperator(null, null);
        return new HireOwnerContext(
            OwnerSubject: ownerSubject,
            TenantId: tenantId,
            OperatorId: operatorId,
            TemplateId: string.Empty,
            TemplateName: string.Empty,
            EmployeeId: null);
    }

    private string ResolveSandboxRole(string hireId)
    {
        if (hireOwners.TryGetValue(hireId, out var ownerContext) &&
            string.Equals(ownerContext.TemplateId, EvaluationWorkspaceTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return "evaluation-evaluator";
        }

        return "hiring";
    }

    private static bool TryParseOwnerSubject(string ownerSubject, out string tenantId, out string operatorId)
    {
        tenantId = string.Empty;
        operatorId = string.Empty;
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return false;
        }

        var delimiterIndex = ownerSubject.IndexOf(':');
        if (delimiterIndex <= 0 || delimiterIndex >= ownerSubject.Length - 1)
        {
            return false;
        }

        var tenant = ownerSubject[..delimiterIndex].Trim();
        var oper = ownerSubject[(delimiterIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(oper))
        {
            return false;
        }

        tenantId = tenant;
        operatorId = oper;
        return true;
    }

    private string ResolveOwnerByHireId(string hireId)
    {
        if (TryResolveOwnerContext(hireId, out var ownerContext))
        {
            return ownerContext.OwnerSubject;
        }

        return ResolveOwnerSubject();
    }

    /// <summary>
    /// 解析当前请求的所有者标识（ownerSubject）。
    /// 优先级：JWT sub claim > X-HireBot-Owner header > tenant:operator fallback。
    /// 注意：fallback 格式包含冒号，需要在传递给 Kubernetes 时进行转义（见 OpenSandboxProvisioner.ToK8sLabelValue）。
    /// </summary>
    /// <param name="tenantId">可选的租户 ID，用于 fallback</param>
    /// <param name="operatorId">可选的操作员 ID，用于 fallback</param>
    /// <returns>所有者标识字符串</returns>
    private string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var sub =
            user?.FindFirst("sub")?.Value ??
            user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
        {
            return sub.Trim();
        }

        var ownerHeader = httpContextAccessor.HttpContext?.Request.Headers["X-HireBot-Owner"].ToString();
        if (!string.IsNullOrWhiteSpace(ownerHeader))
        {
            return ownerHeader.Trim();
        }

        var (resolvedTenantId, resolvedOperatorId) = ResolveTenantAndOperator(tenantId, operatorId);
        return $"{resolvedTenantId}:{resolvedOperatorId}";
    }

    /// <summary>
    /// 解析租户 ID 和操作员 ID。
    /// 优先从参数、JWT claims 中提取，最后 fallback 到默认值。
    /// </summary>
    /// <param name="tenantId">可选的租户 ID</param>
    /// <param name="operatorId">可选的操作员 ID</param>
    /// <returns>租户 ID 和操作员 ID 的元组</returns>
    private (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
    {
        var user = httpContextAccessor.HttpContext?.User;

        var resolvedTenantId = FirstNonEmpty(
            tenantId,
            user?.FindFirst("tenant_id")?.Value,
            user?.FindFirst("tenant")?.Value,
            user?.FindFirst("tid")?.Value,
            "tenant-default");

        var resolvedOperatorId = FirstNonEmpty(
            operatorId,
            user?.FindFirst("operator_id")?.Value,
            user?.FindFirst("preferred_username")?.Value,
            user?.FindFirst(ClaimTypes.Name)?.Value,
            user?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            "operator-default");

        return (resolvedTenantId, resolvedOperatorId);
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryNormalizeArtifactPath(string artifactPath, out string normalizedArtifactPath, out string error)
    {
        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName cannot be empty";
            return false;
        }

        var segments = artifactPath
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName is invalid";
            return false;
        }

        if (segments.Any(static segment =>
                string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName is invalid";
            return false;
        }

        normalizedArtifactPath = string.Join('/', segments);
        error = string.Empty;
        return true;
    }

    private static string ResolveArtifactContentType(string artifactPath)
    {
        var extension = Path.GetExtension(artifactPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return extension.ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".md" => "text/markdown; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".yaml" or ".yml" => "application/yaml",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    private static bool TryNormalizeHireId(string hireId, out string normalizedHireId, out string error)
    {
        if (string.IsNullOrWhiteSpace(hireId))
        {
            normalizedHireId = string.Empty;
            error = "hireId 不能为空";
            return false;
        }

        normalizedHireId = hireId.Trim();
        error = string.Empty;
        return true;
    }

    private sealed record ProvisionedSandboxBinding(
        string HireId,
        string SandboxId,
        string State,
        string? GatewayEndpoint);

    private sealed record SandboxGatewayTarget(
        string SandboxId,
        string GatewayEndpoint);

    private sealed record SystemSkillUploadPayload(
        string SkillId,
        string SkillVersion,
        string SkillHash,
        IReadOnlyList<SystemSkillFileUploadPayload> Files,
        IReadOnlyList<SystemSkillStageRuleUploadPayload> StageRules);

    private sealed record SystemSkillFileUploadPayload(
        string RelativePath,
        string ContentHash,
        string Content);

    private sealed record SystemSkillStageRuleUploadPayload(
        string Stage,
        string SkillName,
        string Description,
        IReadOnlyList<string> RequiredFields);

    private sealed record SystemSkillUploadResult(
        string HireId,
        string SandboxId,
        string SkillId,
        string SkillVersion,
        string SkillHash,
        string InstalledPath,
        IReadOnlyList<StageSkillMappingDto> LoadedStageSkills);

    private sealed record TemplatePackageUploadResult(
        string HireId,
        string SandboxId,
        string PackageId,
        string PackageVersion,
        string PackageHash,
        string InstalledPath);

    private sealed record DigitalEmployeeUploadResponse(
        bool Success,
        string? Error,
        string? Name,
        int SkillsInstalled,
        IReadOnlyList<string>? InstalledFiles,
        int? TotalSkillsLoaded);

    private sealed record HireOwnerContext(
        string OwnerSubject,
        string TenantId,
        string OperatorId,
        string TemplateId,
        string TemplateName,
        string? EmployeeId);

    private sealed record RemoteCallResult<T>(bool Success, int StatusCode, string Message, T? Data)
    {
        public static RemoteCallResult<T> Ok(T data)
        {
            return new RemoteCallResult<T>(true, 200, string.Empty, data);
        }

        public static RemoteCallResult<T> Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "调用下游服务失败" : message;
            return new RemoteCallResult<T>(false, normalizedStatusCode, normalizedMessage, default);
        }
    }
    private sealed record RemoteBinaryCallResult(bool Success, int StatusCode, string Message, string? FileName, string? ContentType, byte[]? Data)
    {
        public static RemoteBinaryCallResult Ok(string fileName, string contentType, byte[] data)
        {
            return new RemoteBinaryCallResult(true, 200, string.Empty, fileName, contentType, data);
        }

        public static RemoteBinaryCallResult Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "璋冪敤涓嬫父鏈嶅姟澶辫触" : message;
            return new RemoteBinaryCallResult(false, normalizedStatusCode, normalizedMessage, null, null, null);
        }
    }
}
