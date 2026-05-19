using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

internal sealed partial class EvaluationService(
    IEmployeeHiringService employeeHiringService,
    IHiringArtifactPackageService artifactPackageService,
    ISandboxService sandboxService,
    IRequestContextService requestContextService,
    HireBotDbContext dbContext,
    IEvaluationAssetStore evaluationAssetStore,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILogger<EvaluationService> logger,
    KingCrabSandboxTokenProvider sandboxTokenProvider,
    ITemplatePackageProvider templatePackageProvider,
    FileSystemTemplatePackageProvider fileSystemTemplatePackageProvider) : IEvaluationService
{
    private static readonly Lazy<IReadOnlyDictionary<string, FixtureTemplateBinding>> FixtureTemplateBindings =
        new(LoadFixtureTemplateBindings);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, decimal> DefaultOntologyWeights =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["accuracy"] = 0.35m,
            ["completeness"] = 0.25m,
            ["compliance"] = 0.2m,
            ["communication"] = 0.2m
        };

    private static readonly IReadOnlyList<string> DefaultOntologyRules =
    [
        "Accuracy: response should align with scenario facts and expected intent.",
        "Completeness: response should cover required process steps.",
        "Compliance: response should follow policy and safety constraints.",
        "Communication: response should be clear, polite, and actionable."
    ];

    private readonly string evaluationResourceRoot =
        ResolveEvaluationResourceRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:EvaluationResourceRoot"]);

    private readonly string evaluationTemplatePackageRoot =
        ResolveEvaluationTemplatePackageRoot(hostEnvironment.ContentRootPath, configuration["HireBot:DigitalEmployeeTemplatesRoot"]);

    private readonly bool _evaluationTemplatePackageRootValid = LogTemplatePackageRoot(
        ResolveEvaluationTemplatePackageRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DigitalEmployeeTemplatesRoot"]),
        logger);

    private static bool LogTemplatePackageRoot(string templateRoot, ILogger logger)
    {
        var exists = Directory.Exists(templateRoot);
        var manifestExists = exists && File.Exists(Path.Combine(templateRoot, "manifest.json"));
        if (!exists)
            logger.LogError("[Eval] CRITICAL: evaluation-expert template root not found: {Path}", templateRoot);
        else if (!manifestExists)
            logger.LogWarning("[Eval] evaluation-expert template root missing manifest.json: {Path}", templateRoot);
        else
            logger.LogInformation("[Eval] Template package root: {Path}, exists=True, manifest=True", templateRoot);
        return exists && manifestExists;
    }

    private Task<ApiResponse<StartHiringConversationResultDto>> EnsureSandboxConversationStartedAsync(
        string owner,
        string hireId,
        string sandboxId,
        string sandboxRole,
        CancellationToken cancellationToken)
    {
        var (tenantId, operatorId) = ResolveTenantAndOperator(owner);
        return sandboxService.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = sandboxRole,
                OwnerSubject = owner,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = sandboxId,
                SessionKey = "default"
            },
            cancellationToken);
    }

    private Task<ApiResponse<HiringConversationTimelineDto>> GetSandboxTimelineAsync(
        string owner,
        string hireId,
        string sandboxId,
        string sandboxRole,
        CancellationToken cancellationToken)
    {
        var (tenantId, operatorId) = ResolveTenantAndOperator(owner);
        return sandboxService.GetTimelineAsync(
            new SandboxTimelineRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = sandboxRole,
                OwnerSubject = owner,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = sandboxId,
                SessionKey = "default"
            },
            cancellationToken);
    }

    private Task<ApiResponse<HiringConversationResultDto>> SendSandboxMessageAsync(
        string owner,
        string hireId,
        string sandboxId,
        string sandboxRole,
        HiringConversationMessageRequestDto request,
        CancellationToken cancellationToken)
    {
        var (tenantId, operatorId) = ResolveTenantAndOperator(owner);
        return sandboxService.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = sandboxRole,
                OwnerSubject = owner,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = sandboxId,
                SessionKey = "default",
                Content = request.Content,
                StructuredAnswers = request.StructuredAnswers,
                Materials = request.Materials
            },
            cancellationToken);
    }

    private static (string TenantId, string OperatorId) ResolveTenantAndOperator(string ownerSubject)
    {
        if (!string.IsNullOrWhiteSpace(ownerSubject))
        {
            var delimiterIndex = ownerSubject.IndexOf(':');
            if (delimiterIndex > 0 && delimiterIndex < ownerSubject.Length - 1)
            {
                var tenantId = ownerSubject[..delimiterIndex].Trim();
                var operatorId = ownerSubject[(delimiterIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(operatorId))
                {
                    return (tenantId, operatorId);
                }
            }
        }

        return (ownerSubject, ownerSubject);
    }

    private static readonly JsonSerializerOptions RuntimeSnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record EvaluationAccessContext(
        EmployeeDetailDto Employee,
        string RequestOwner,
        string PersistenceScope);

    /// <summary>
    /// 从 DB 按 owner + employeeId 查询员工快照（替代 store.GetAsync）。
    /// </summary>
    private async Task<EmployeeDetailDto?> GetEmployeeFromDbAsync(string owner, string employeeId, CancellationToken cancellationToken)
    {
        var normalizedScope = owner.Trim();
        var instance = await dbContext.Instances
            .AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.InstanceId == employeeId &&
                (i.OwnerUserId == normalizedScope ||
                 (i.InstanceType == "department" && i.TenantId == normalizedScope)),
                cancellationToken);
        if (instance is null || string.IsNullOrWhiteSpace(instance.RuntimeSnapshotJson))
            return null;
        try { return JsonSerializer.Deserialize<EmployeeDetailDto>(instance.RuntimeSnapshotJson, RuntimeSnapshotJsonOptions); }
        catch { return null; }
    }

    private async Task<EvaluationAccessContext?> ResolveEvaluationAccessContextAsync(
        string employeeId,
        CancellationToken cancellationToken)
    {
        var normalizedEmployeeId = employeeId.Trim();
        var requestOwner = requestContextService.ResolveOwnerSubject();
        var employee = await GetEmployeeFromDbAsync(requestOwner, normalizedEmployeeId, cancellationToken);
        if (employee is not null)
        {
            return new EvaluationAccessContext(
                employee,
                requestOwner,
                ResolveEvaluationPersistenceScope(employee, requestOwner));
        }

        var (tenantId, _) = requestContextService.ResolveTenantAndOperator(null, null);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        employee = await GetEmployeeFromDbAsync(tenantId.Trim(), normalizedEmployeeId, cancellationToken);
        if (employee is null || !string.Equals(employee.InstanceType, "department", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new EvaluationAccessContext(
            employee,
            requestOwner,
            ResolveEvaluationPersistenceScope(employee, requestOwner));
    }

    private string ResolveEvaluationPersistenceScope(EmployeeDetailDto employee, string requestOwner)
    {
        if (!string.Equals(employee.InstanceType, "department", StringComparison.OrdinalIgnoreCase))
        {
            return requestOwner;
        }

        var (tenantId, _) = requestContextService.ResolveTenantAndOperator(null, null);
        return string.IsNullOrWhiteSpace(tenantId) ? requestOwner : tenantId.Trim();
    }

    /// <summary>
    /// 将更新后的员工快照写回 DB（替代 store.UpsertAsync）。
    /// </summary>
    private async Task SaveEmployeeToDbAsync(EmployeeDetailDto employee, CancellationToken cancellationToken)
    {
        var instance = await dbContext.Instances
            .FirstOrDefaultAsync(i => i.InstanceId == employee.EmployeeId, cancellationToken);
        if (instance is null) return;
        instance.Status = employee.Status ?? instance.Status;
        instance.RuntimeSnapshotJson = JsonSerializer.Serialize(employee, RuntimeSnapshotJsonOptions);
        instance.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ApiResponse<EvaluationWorkspaceStatusDto>> GetWorkspaceStatusAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return ApiResponse<EvaluationWorkspaceStatusDto>.ErrorResponse(400, "employeeId cannot be empty");

        var accessContext = await ResolveEvaluationAccessContextAsync(employeeId, cancellationToken);
        if (accessContext is null)
            return ApiResponse<EvaluationWorkspaceStatusDto>.ErrorResponse(404, "employee not found");

        var employee = accessContext.Employee;
        var scope = accessContext.PersistenceScope;

        var ctx = await LoadWorkspaceContextAsync(scope, employee.EmployeeId, cancellationToken);
        if (ctx is null || ctx.StepStates.Count == 0)
        {
            return ApiResponse<EvaluationWorkspaceStatusDto>.SuccessResponse(
                new EvaluationWorkspaceStatusDto(
                    EmployeeId: employeeId.Trim(),
                    OverallStatus: "not_started",
                    TargetSandboxId: null,
                    EvaluatorSandboxId: null,
                    EvaluatorRuntimeId: null,
                    TargetRuntimeId: null,
                    SessionId: null,
                    TargetGatewayEndpoint: null,
                    EvaluatorGatewayEndpoint: null,
                    Steps: [],
                    ErrorMessage: null));
        }

        var sandboxIds = new[] { ctx.TargetSandboxId, ctx.EvaluatorSandboxId }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
        var sandboxEndpoints = sandboxIds.Length == 0
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : await dbContext.SandboxInstances
                .AsNoTracking()
                .Where(item => sandboxIds.Contains(item.SandboxId))
                .ToDictionaryAsync(
                    item => item.SandboxId,
                    item => string.IsNullOrWhiteSpace(item.GatewayEndpoint) ? null : item.GatewayEndpoint.Trim(),
                    StringComparer.OrdinalIgnoreCase,
                    cancellationToken);

        var steps = ctx.StepStates
            .OrderBy(item => item.Key switch
            {
                "target_sandbox" => 0,
                "upload_target_template" => 1,
                "evaluator_sandbox" => 2,
                "upload_skill" => 3,
                "upload_employee_template" => 4,
                "upload_artifacts" => 5,
                "materials" => 6,
                _ => 99
            })
            .Select(item => new EvaluationWorkspaceStepDto(
                Step: item.Key,
                Status: item.Value.Status,
                Detail: item.Value.Detail))
            .ToArray();

        var hasFailed = steps.Any(item => item.Status == "failed");
        var allCompleted = steps.All(item => item.Status == "completed");
        var overallStatus = hasFailed ? "failed"
            : allCompleted && ctx.SkillLoadedAtUtc is not null ? "ready"
            : "creating";

        return ApiResponse<EvaluationWorkspaceStatusDto>.SuccessResponse(
            new EvaluationWorkspaceStatusDto(
                EmployeeId: employeeId.Trim(),
                OverallStatus: overallStatus,
                TargetSandboxId: string.IsNullOrWhiteSpace(ctx.TargetSandboxId) ? null : ctx.TargetSandboxId,
                EvaluatorSandboxId: string.IsNullOrWhiteSpace(ctx.EvaluatorSandboxId) ? null : ctx.EvaluatorSandboxId,
                EvaluatorRuntimeId: string.IsNullOrWhiteSpace(ctx.EvaluatorHireId) ? null : ctx.EvaluatorHireId,
                TargetRuntimeId: string.IsNullOrWhiteSpace(ctx.TargetHireId) ? null : ctx.TargetHireId,
                SessionId: string.IsNullOrWhiteSpace(ctx.SessionId) ? null : ctx.SessionId,
                TargetGatewayEndpoint: sandboxEndpoints.GetValueOrDefault(ctx.TargetSandboxId),
                EvaluatorGatewayEndpoint: sandboxEndpoints.GetValueOrDefault(ctx.EvaluatorSandboxId),
                Steps: steps,
                ErrorMessage: steps.FirstOrDefault(item => item.Status == "failed")?.Detail));
    }

    public async Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(400, "employeeId cannot be empty");
        }

        var normalizedEmployeeId = employeeId.Trim();
        var accessContext = await ResolveEvaluationAccessContextAsync(normalizedEmployeeId, cancellationToken);
        if (accessContext is null)
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(404, "employee not found");
        }

        var employee = accessContext.Employee;
        var scope = accessContext.PersistenceScope;

        // 无论评估处于哪个阶段，都尝试读取已保存的 workspace 上下文，以便返回测试案例大纲
        var workspaceContext = await LoadWorkspaceContextAsync(scope, normalizedEmployeeId, cancellationToken);
        var testcaseOutlines = workspaceContext?.TestcaseOutlines
            ?.Select(o => new EvaluationTestcaseOutlineDto(o.TestcaseId, o.Title, o.UserRequest))
            .ToArray();

        var normalizedEmployeeStatus = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired";
        var normalizedEvalPhase = employee.EvalPhase?.Trim().ToLowerInvariant();
        var isPrivateBranch = string.Equals(employee.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase);
        var shouldHideHistoricalSessionState =
            // 普通/雇佣评估沿用旧逻辑：尚未真正进入评估阶段时，不展示历史 EvaluationSession。
            // 私有分支是特殊评估：实例始终保持 live，创建后 EvalPhase 可能为空，但仍需要继续读取
            // EvaluationSession/Readiness，避免评估页一直停在 not_started。
            !isPrivateBranch &&
            (string.Equals(normalizedEmployeeStatus, "hired", StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(normalizedEvalPhase) ||
             string.Equals(normalizedEvalPhase, "pending_skill_upload", StringComparison.OrdinalIgnoreCase));
        if (shouldHideHistoricalSessionState)
        {
            var initialState = new EvaluationStateDto(
                EmployeeId: normalizedEmployeeId,
                OverallStatus: "not_started",
                Scenarios: [],
                Recommendation: "No evaluation execution yet. Start AI evaluation first.",
                SessionId: null,
                Readiness: null,
                QuestionCards: null,
                LatestReport: null,
                AssetRefs: null,
                TestcaseOutlines: testcaseOutlines);
            return ApiResponse<EvaluationStateDto>.SuccessResponse(initialState);
        }

        var latestSession = await dbContext.EvaluationSessions
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == scope &&
                item.EmployeeId == normalizedEmployeeId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        EvaluationReadinessDto? readiness = null;
        EvaluationReportSummaryDto? latestReport = null;
        IReadOnlyList<EvaluationQuestionCardDto>? questionCards = null;
        IReadOnlyList<EvaluationAssetRefDto>? assetRefs = null;
        IReadOnlyList<EvaluationScenarioDto> scenarios = [];
        var overallStatus = "not_started";
        var recommendation = "No evaluation execution yet. Start AI evaluation first.";

        if (latestSession is not null)
        {
            var assets = await dbContext.EvaluationAssets
                .AsNoTracking()
                .Where(item => item.SessionEntityId == latestSession.Id)
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var testcaseReady = assets.Any(item => item.AssetType == "testcases-json");
            var ontologyReady = assets.Any(item => item.AssetType == "ontology-json");
            readiness = BuildReadiness(testcaseReady, ontologyReady);

            assetRefs = assets
                .Take(30)
                .Select(ToAssetRef)
                .ToArray();

            var testcaseAssets = assets
                .Where(item => item.AssetType == "testcases-json")
                .GroupBy(
                    item => string.IsNullOrWhiteSpace(item.RelatedKey) ? item.RelativePath : item.RelatedKey,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .First())
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(5)
                .ToArray();
            var cards = await BuildQuestionCardsFromAssetsAsync(testcaseAssets, cancellationToken);
            if (cards.Count > 0)
            {
                questionCards = cards;
            }

            var reportEntity = await dbContext.EvaluationReports
                .AsNoTracking()
                .Where(item => item.SessionEntityId == latestSession.Id)
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (reportEntity is not null)
            {
                var reportJsonUrl = assets.FirstOrDefault(item => item.Id == reportEntity.ReportJsonAssetId)?.PublicUrl ?? string.Empty;
                var reportHtmlUrl = assets.FirstOrDefault(item => item.Id == reportEntity.ReportHtmlAssetId)?.PublicUrl;
                latestReport = new EvaluationReportSummaryDto(
                    ReportId: reportEntity.Id.ToString("N"),
                    Iteration: reportEntity.Iteration,
                    OverallScore: reportEntity.OverallScore,
                    Passed: reportEntity.Passed,
                    ReportJsonUrl: reportJsonUrl,
                    ReportHtmlUrl: reportHtmlUrl,
                    CreatedAtUtc: reportEntity.CreatedAtUtc.ToString("o"),
                    DimensionScores: DeserializeDimensionScores(reportEntity.DimensionScoresJson));
            }

            var normalizedSessionStatus = NormalizeEvaluationStatus(latestSession.Status);
            overallStatus = latestReport is null
                ? normalizedSessionStatus
                : latestReport.Passed
                    ? "passed"
                    : "failed";
            recommendation = BuildEvaluationRecommendation(latestReport, readiness, normalizedSessionStatus);

            scenarios = BuildScenariosFromQuestionCards(
                questionCards ?? [],
                latestSession,
                latestReport);
            if (scenarios.Count == 0 && latestReport is not null)
            {
                scenarios = [BuildSummaryScenario(latestSession, latestReport)];
            }
        }

        var state = new EvaluationStateDto(
            EmployeeId: normalizedEmployeeId,
            OverallStatus: overallStatus,
            Scenarios: scenarios,
            Recommendation: recommendation,
            SessionId: latestSession?.SessionId,
            Readiness: readiness,
            QuestionCards: questionCards,
            LatestReport: latestReport,
            AssetRefs: assetRefs,
            TestcaseOutlines: testcaseOutlines);

        return ApiResponse<EvaluationStateDto>.SuccessResponse(state);
    }

    public async Task<ApiResponse<EvaluationSandboxConversationStateDto>> GetEvaluationSandboxConversationAsync(
        string employeeId,
        string? since = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(400, "employeeId cannot be empty");
        }

        var accessContext = await ResolveEvaluationAccessContextAsync(employeeId, cancellationToken);
        if (accessContext is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(404, "employee not found");
        }

        var owner = accessContext.RequestOwner;
        var scope = accessContext.PersistenceScope;
        var employee = accessContext.Employee;

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionResult = await EnsureEvaluatorConversationStartedAsync(owner, workspaceResult.Data, cancellationToken);
        if (!sessionResult.Success || sessionResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(sessionResult.Code, sessionResult.Message);
        }

        var conversationPreparedResult = await EnsureSupplementConversationPreparedAsync(
            owner,
            employee,
            sessionResult.Data,
            cancellationToken);
        if (!conversationPreparedResult.Success || conversationPreparedResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(
                conversationPreparedResult.Code,
                conversationPreparedResult.Message);
        }

        var timelineResult = await GetSandboxTimelineAsync(
            owner,
            conversationPreparedResult.Data.EvaluatorHireId,
            conversationPreparedResult.Data.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        // Short-circuit: if since matches the latest message ID, return 304
        if (!string.IsNullOrWhiteSpace(since) && timelineResult.Data.Messages.Count > 0)
        {
            var latestId = timelineResult.Data.Messages[^1].MessageId;
            if (string.Equals(latestId, since.Trim(), StringComparison.Ordinal))
            {
                return ApiResponse<EvaluationSandboxConversationStateDto>.NotModified();
            }
        }

        var refreshedWorkspace = conversationPreparedResult.Data with { SessionId = timelineResult.Data.SessionId };
        await SaveWorkspaceContextAsync(scope, employee.EmployeeId, refreshedWorkspace, cancellationToken);

        var questionCards = await LoadQuestionCardsForLatestSessionAsync(scope, employee.EmployeeId, cancellationToken);

        return ApiResponse<EvaluationSandboxConversationStateDto>.SuccessResponse(
            BuildSandboxConversationState(employee, refreshedWorkspace, timelineResult.Data, questionCards));
    }

    public async Task<ApiResponse<EvaluationSandboxConversationStateDto>> SendEvaluationSandboxMessageAsync(
        string employeeId,
        EvaluationSandboxMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(400, "employeeId and content are required");
        }

        var accessContext = await ResolveEvaluationAccessContextAsync(employeeId, cancellationToken);
        if (accessContext is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(404, "employee not found");
        }

        var owner = accessContext.RequestOwner;
        var scope = accessContext.PersistenceScope;
        var employee = accessContext.Employee;

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            request.SkillRootPath,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionResult = await EnsureEvaluatorConversationStartedAsync(owner, workspaceResult.Data, cancellationToken);
        if (!sessionResult.Success || sessionResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(sessionResult.Code, sessionResult.Message);
        }

        var conversationPreparedResult = await EnsureSupplementConversationPreparedAsync(
            owner,
            employee,
            sessionResult.Data,
            cancellationToken);
        if (!conversationPreparedResult.Success || conversationPreparedResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(
                conversationPreparedResult.Code,
                conversationPreparedResult.Message);
        }

        var sendRequest = new HiringConversationMessageRequestDto
        {
            Content = request.Content.Trim(),
            StructuredAnswers = request.StructuredAnswers,
            Materials = request.Materials
        };
        var sendResult = await SendSandboxMessageAsync(
            owner,
            conversationPreparedResult.Data.EvaluatorHireId,
            conversationPreparedResult.Data.EvaluatorSandboxId,
            "evaluation-evaluator",
            sendRequest,
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        var timelineResult = await GetSandboxTimelineAsync(
            owner,
            conversationPreparedResult.Data.EvaluatorHireId,
            conversationPreparedResult.Data.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        var refreshedWorkspace = conversationPreparedResult.Data with { SessionId = timelineResult.Data.SessionId };
        await SaveWorkspaceContextAsync(scope, employee.EmployeeId, refreshedWorkspace, cancellationToken);

        var questionCards = await LoadQuestionCardsForLatestSessionAsync(scope, employee.EmployeeId, cancellationToken);

        return ApiResponse<EvaluationSandboxConversationStateDto>.SuccessResponse(
            BuildSandboxConversationState(employee, refreshedWorkspace, timelineResult.Data, questionCards),
            "evaluation sandbox replied");
    }

    public async Task<ApiResponse<EmployeeDetailDto>> StartAiEvaluationAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId is required");
        }

        var accessContext = await ResolveEvaluationAccessContextAsync(employeeId, cancellationToken);
        if (accessContext is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "employee not found");
        }

        var owner = accessContext.RequestOwner;
        var scope = accessContext.PersistenceScope;
        var employee = accessContext.Employee;

        var currentStatus = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired";
        var isPrivateBranch = string.Equals(employee.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase);

        // 私有分支始终保持 live，只通过 EvalPhase 表示评估阶段；
        // 普通/雇佣员工只能从 hired/failed/interning_ai 发起 START。
        if (currentStatus is not ("hired" or "failed" or "interning_ai") &&
            !(isPrivateBranch && string.Equals(currentStatus, "live", StringComparison.OrdinalIgnoreCase)))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, $"current status does not allow START: {currentStatus}");
        }

        // 启动两个评估沙箱并上传所需文件；文件传好后沙箱内部自行处理评估逻辑。
        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            forceTargetHireRecreate: true,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        await StartNewEvaluationSessionAsync(scope, employee.EmployeeId, workspaceResult.Data, cancellationToken);

        var updated = employee with
        {
            // 私有分支不改变 status，保持 live；普通/雇佣评估进入 interning_ai。
            Status = isPrivateBranch ? "live" : "interning_ai",
            LifecycleStatus = isPrivateBranch ? employee.LifecycleStatus : "AI evaluation",
            EvalPhase = "ai_running",
        };

        logger.LogInformation("[Eval] START completed employeeId={EmployeeId}", employee.EmployeeId);
        await SaveEmployeeToDbAsync(updated, cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "Evaluation workspace ready");
    }

    public async Task<ApiResponse<EmployeeDetailDto>> SubmitOnboardingDecisionAsync(
        string employeeId,
        EvaluationOnboardingDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null || string.IsNullOrWhiteSpace(request.Decision))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId and decision are required");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await GetEmployeeFromDbAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "employee not found");
        }

        var currentStatus = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired";
        if (currentStatus != "interning_human")
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, $"current status does not allow onboarding decision: {currentStatus}");
        }

        var decision = request.Decision.Trim().ToUpperInvariant();
        var updated = decision switch
        {
            "ONBOARD" => employee with
            {
                Status = "interning_human",
                LifecycleStatus = "pending onboarding",
                EvalPhase = "pending_onboarding",
                StageSummary = "Human review passed, waiting for identity setup and onboarding",
                PrimarySignal = "Pending action: finish identity setup and onboarding",
                SignalLevel = "ok",
                PendingActions = ["Complete identity setup and onboarding"]
            },
            "FORCE" => employee with
            {
                Status = "interning_human",
                LifecycleStatus = "pending onboarding",
                EvalPhase = "pending_onboarding_force",
                StageSummary = "Human review force-passed, waiting for onboarding",
                PrimarySignal = "Pending action: finish identity setup and onboarding (force mode)",
                SignalLevel = "warn",
                PendingActions = ["Complete identity setup and onboarding"]
            },
            "REJECT" => employee with
            {
                Status = "failed",
                LifecycleStatus = "evaluation failed",
                EvalPhase = "pending_review",
                StageSummary = "Human review rejected, go to Review for rollback or continue hire",
                PrimarySignal = "Pending action: choose a Review fallback path",
                SignalLevel = "error",
                PendingActions = ["Go to Review and choose rollback option"]
            },
            _ => null
        };

        if (updated is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "decision only supports ONBOARD, REJECT, FORCE");
        }

        await SaveEmployeeToDbAsync(updated, cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "human evaluation decision submitted");
    }

    private async Task<ApiResponse<EvaluationFetchTestcasesResultDto>> FetchTestcasesAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(400, "employeeId cannot be empty");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var normalizedEmployeeId = employeeId.Trim();
        var employee = await GetEmployeeFromDbAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceResult.Data, cancellationToken);

        var sourceFiles = await LoadTestcaseSourcesAsync(workspaceResult.Data, employee, cancellationToken);
        if (sourceFiles.Count == 0)
        {
            await UpdateSessionStatusAsync(sessionEntity, "waiting_materials", "no testcase source found", cancellationToken);
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(422, "no testcase source found");
        }

        var parsedTestcases = new List<ParsedTestcase>();
        var assetRefs = new List<EvaluationAssetRefDto>();
        foreach (var sourceFile in sourceFiles)
        {
            var parsedFromFile = ParseTestcases(sourceFile.FileName, sourceFile.SourcePath, sourceFile.RawJson);
            if (parsedFromFile.Count == 0)
            {
                continue;
            }

            parsedTestcases.AddRange(parsedFromFile);
            var testcaseAsset = await PersistTextAssetAsync(
                sessionEntity,
                assetType: "testcases-json",
                relatedKey: $"file:{sourceFile.FileName}",
                fileName: sourceFile.FileName,
                content: sourceFile.RawJson,
                mimeType: "application/json",
                sourceType: sourceFile.SourceType,
                cancellationToken);
            assetRefs.Add(ToAssetRef(testcaseAsset));
        }

        if (parsedTestcases.Count == 0)
        {
            await UpdateSessionStatusAsync(sessionEntity, "waiting_materials", "failed to parse testcase files", cancellationToken);
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(422, "failed to parse testcase files");
        }

        var questionCards = BuildQuestionCards(parsedTestcases);
        await UpdateSessionStatusAsync(sessionEntity, "testcases_ready", null, cancellationToken);

        var result = new EvaluationFetchTestcasesResultDto(
            SessionId: sessionEntity.SessionId,
            TargetHireId: workspaceResult.Data.TargetHireId,
            TargetRuntimeId: workspaceResult.Data.TargetHireId,
            Testcases: parsedTestcases
                .Select(item => new EvaluationTestcaseDto(
                    TestcaseId: item.TestcaseId,
                    ScenarioName: item.ScenarioName,
                    SourceFile: item.SourceFile,
                    SourcePath: item.SourcePath,
                    RawJson: item.RawJson,
                    ExpectedSteps: item.ExpectedSteps))
                .ToArray(),
            QuestionCards: questionCards,
            Assets: assetRefs);

        return ApiResponse<EvaluationFetchTestcasesResultDto>.SuccessResponse(result, "testcases loaded");
    }

    private async Task<ApiResponse<EvaluationOntologyQueryResultDto>> QueryOntologyAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationOntologyQueryResultDto>.ErrorResponse(400, "employeeId cannot be empty");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var normalizedEmployeeId = employeeId.Trim();
        var employee = await GetEmployeeFromDbAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationOntologyQueryResultDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationOntologyQueryResultDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceResult.Data, cancellationToken);

        var ontologyProfile = await BuildOntologyProfileAsync(workspaceResult.Data, employee, cancellationToken);
        var payload = new
        {
            version = "ontology-v2",
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
            source = ontologyProfile.SourceSummary,
            sourceFiles = ontologyProfile.Sources
                .Select(item => new
                {
                    fileName = item.FileName,
                    sourcePath = item.SourcePath,
                    sourceType = item.SourceType
                })
                .ToArray(),
            dimensions = ontologyProfile.DimensionWeights,
            rules = ontologyProfile.DimensionRules
        };
        var ontologyJson = JsonSerializer.Serialize(payload, JsonOptions);
        var ontologyAsset = await PersistTextAssetAsync(
            sessionEntity,
            assetType: "ontology-json",
            relatedKey: "ontology:resolved",
            fileName: "evaluation_ontology_resolved.json",
            content: ontologyJson,
            mimeType: "application/json",
            sourceType: "system",
            cancellationToken);
        await UpdateSessionStatusAsync(sessionEntity, "ontology_ready", null, cancellationToken);

        var result = new EvaluationOntologyQueryResultDto(
            SessionId: sessionEntity.SessionId,
            DimensionWeights: ontologyProfile.DimensionWeights,
            DimensionRules: ontologyProfile.DimensionRules,
            Assets: [ToAssetRef(ontologyAsset)]);

        return ApiResponse<EvaluationOntologyQueryResultDto>.SuccessResponse(result, "ontology loaded");
    }

    private async Task<ApiResponse<EvaluationReportUpsertResultDto>> UpsertReportAsync(
        string employeeId,
        EvaluationReportUpsertRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) ||
            request is null ||
            string.IsNullOrWhiteSpace(request.SessionId))
        {
            return ApiResponse<EvaluationReportUpsertResultDto>.ErrorResponse(400, "employeeId and sessionId are required");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var normalizedEmployeeId = employeeId.Trim();
        var employee = await GetEmployeeFromDbAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationReportUpsertResultDto>.ErrorResponse(404, "employee not found");
        }

        var sessionEntity = await dbContext.EvaluationSessions
            .Where(item =>
                item.OwnerSubject == owner &&
                item.EmployeeId == normalizedEmployeeId &&
                item.SessionId == request.SessionId.Trim())
            .FirstOrDefaultAsync(cancellationToken);
        if (sessionEntity is null)
        {
            return ApiResponse<EvaluationReportUpsertResultDto>.ErrorResponse(404, "evaluation session not found");
        }

        var dimensionScores = request.DimensionScores?.Count > 0
            ? request.DimensionScores
            : [new EvaluationDimensionScoreDto("overall", request.OverallScore, "No dimension score provided", ["system"])];
        var reportId = $"report_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        var payload = new
        {
            reportId,
            sessionId = sessionEntity.SessionId,
            employeeId = normalizedEmployeeId,
            targetHireId = sessionEntity.TargetHireId,
            evaluatorHireId = sessionEntity.EvaluatorHireId,
            iteration = sessionEntity.Iteration,
            overallScore = request.OverallScore,
            passed = request.Passed,
            summary = request.Summary,
            dimensionScores,
            generatedAtUtc = now.ToString("o")
        };
        var reportJson = JsonSerializer.Serialize(payload, JsonOptions);
        var reportHtml = BuildReportHtml(payload, dimensionScores);

        var reportJsonAsset = await PersistTextAssetAsync(
            sessionEntity,
            assetType: "report-json",
            relatedKey: reportId,
            fileName: $"evaluation_report_{reportId}.json",
            content: reportJson,
            mimeType: "application/json",
            sourceType: "evaluator",
            cancellationToken);
        var reportHtmlAsset = await PersistTextAssetAsync(
            sessionEntity,
            assetType: "report-html",
            relatedKey: reportId,
            fileName: $"evaluation_report_{reportId}.html",
            content: reportHtml,
            mimeType: "text/html",
            sourceType: "evaluator",
            cancellationToken);

        var reportEntity = new EvaluationReportEntity
        {
            Id = Guid.NewGuid(),
            SessionEntityId = sessionEntity.Id,
            Iteration = sessionEntity.Iteration,
            OverallScore = request.OverallScore,
            Passed = request.Passed,
            DimensionScoresJson = JsonSerializer.Serialize(dimensionScores, JsonOptions),
            SummaryJson = JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["summary"] = request.Summary,
                    ["generatedAtUtc"] = now
                },
                JsonOptions),
            ReportJsonAssetId = reportJsonAsset.Id,
            ReportHtmlAssetId = reportHtmlAsset.Id,
            CreatedAtUtc = now
        };
        dbContext.EvaluationReports.Add(reportEntity);

        sessionEntity.Status = request.Passed ? "passed" : "failed";
        sessionEntity.LastError = null;
        sessionEntity.Iteration = Math.Max(1, sessionEntity.Iteration) + 1;
        sessionEntity.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var result = new EvaluationReportUpsertResultDto(
            SessionId: sessionEntity.SessionId,
            ReportId: reportEntity.Id.ToString("N"),
            Iteration: reportEntity.Iteration,
            OverallScore: reportEntity.OverallScore,
            Passed: reportEntity.Passed,
            ReportJsonUrl: reportJsonAsset.PublicUrl,
            ReportHtmlUrl: reportHtmlAsset.PublicUrl,
            Assets: [ToAssetRef(reportJsonAsset), ToAssetRef(reportHtmlAsset)]);

        return ApiResponse<EvaluationReportUpsertResultDto>.SuccessResponse(result, "evaluation report persisted");
    }

    public async Task<ApiResponse<EvaluationSandboxConnectionResultDto>> GetSandboxConnectionAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(400, "employeeId cannot be empty");

        var accessContext = await ResolveEvaluationAccessContextAsync(employeeId, cancellationToken);
        if (accessContext is null)
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(404, "employee not found");

        var owner = accessContext.RequestOwner;
        var scope = accessContext.PersistenceScope;
        var employee = accessContext.Employee;

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner, employee, null,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);

        var ctx = workspaceResult.Data;
        var instance = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.SandboxId == ctx.EvaluatorSandboxId, cancellationToken);
        var gatewayEndpoint = instance?.GatewayEndpoint?.Trim();
        if (string.IsNullOrWhiteSpace(gatewayEndpoint))
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(409, "evaluator sandbox gateway endpoint not ready");
        var targetInstance = await dbContext.SandboxInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.SandboxId == ctx.TargetSandboxId, cancellationToken);
        var targetGatewayEndpoint = targetInstance?.GatewayEndpoint?.Trim();
        if (string.IsNullOrWhiteSpace(targetGatewayEndpoint))
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(409, "target sandbox gateway endpoint not ready");

        var token = await sandboxTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(502, "unable to acquire sandbox access token");

        var sessionEntity = await GetOrCreateSessionEntityAsync(scope, employee, ctx, cancellationToken);
        var evaluatorMaterialsResult = await PrepareEvaluatorMaterialsArchiveAsync(
            owner,
            employee,
            ctx,
            sessionEntity,
            cancellationToken);
        if (!evaluatorMaterialsResult.Success || string.IsNullOrWhiteSpace(evaluatorMaterialsResult.Data))
        {
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(
                evaluatorMaterialsResult.Code,
                evaluatorMaterialsResult.Message);
        }

        // 将 evaluation-context.json 直接上传到 evaluator workspace/runtime/ 目录，
        // evaluator skill 可通过固定路径 workspace/runtime/evaluation-context.json 读取，
        // 不再经过媒体缓存中转，路径更稳定可预测。
        var runtimeContextJson = BuildRuntimeContextJson(
            employee,
            ctx,
            sessionEntity,
            targetGatewayEndpoint,
            evaluatorMaterialsResult.Data);
        var runtimeContextBytes = System.Text.Encoding.UTF8.GetBytes(runtimeContextJson);
        var runtimeContextUploadResult = await sandboxService.UploadWorkspaceFileAsync(
            new SandboxWorkspaceUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = ctx.EvaluatorHireId,
                SandboxRole = "evaluation-evaluator",
                OwnerSubject = owner,
                SandboxId = ctx.EvaluatorSandboxId,
                TargetDir = "runtime",
                FileName = "evaluation-context.json",
                Content = runtimeContextBytes,
                ContentType = "application/json"
            },
            cancellationToken);

        string runtimeContextPath;
        if (runtimeContextUploadResult.Success && runtimeContextUploadResult.Data is not null)
        {
            // WorkspaceDir 为上传后文件所在的 workspace 相对目录，拼接文件名得到完整路径
            runtimeContextPath = $"{runtimeContextUploadResult.Data.WorkspaceDir}/evaluation-context.json";
        }
        else
        {
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(502,
                $"failed to upload runtime context to evaluator sandbox workspace: {runtimeContextUploadResult.Message}");
        }

        var payloadJson = BuildLiveEvaluationBootstrapPayload(
            owner,
            employee,
            ctx,
            sessionEntity,
            targetGatewayEndpoint,
            runtimeContextPath);

        logger.LogInformation(
            "[Eval] Sandbox connection ready employeeId={EmployeeId} sessionId={SessionId} targetSandboxId={TargetSandboxId} targetGatewayEndpoint={TargetGatewayEndpoint} evaluatorSandboxId={EvaluatorSandboxId} evaluatorGatewayEndpoint={EvaluatorGatewayEndpoint} runtimeContextPath={RuntimeContextPath}",
            employee.EmployeeId,
            sessionEntity.SessionId,
            ctx.TargetSandboxId,
            targetGatewayEndpoint,
            ctx.EvaluatorSandboxId,
            gatewayEndpoint,
            runtimeContextPath);

        await UpdateSessionStatusAsync(sessionEntity, "ws_connected", null, cancellationToken);

        var result = new EvaluationSandboxConnectionResultDto(
            gatewayEndpoint,
            token,
            ctx.EvaluatorSandboxId,
            ctx.TargetSandboxId,
            sessionEntity.SessionId,
            ctx.TargetHireId,
            targetGatewayEndpoint,
            payloadJson);
        return ApiResponse<EvaluationSandboxConnectionResultDto>.SuccessResponse(result, "sandbox connection info ready");
    }

    public async Task<ApiResponse<EvaluationVerdictSyncResultDto>> SyncVerdictAsync(
        string employeeId,
        EvaluationVerdictSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null || string.IsNullOrWhiteSpace(request.SessionId) || request.Verdict is null)
            return ApiResponse<EvaluationVerdictSyncResultDto>.ErrorResponse(400, "employeeId, sessionId and verdict are required");

        var accessContext = await ResolveEvaluationAccessContextAsync(employeeId, cancellationToken);
        if (accessContext is null)
            return ApiResponse<EvaluationVerdictSyncResultDto>.ErrorResponse(404, "employee not found");

        var scope = accessContext.PersistenceScope;
        var employee = accessContext.Employee;

        var sessionEntity = await dbContext.EvaluationSessions
            .FirstOrDefaultAsync(item =>
                item.OwnerSubject == scope &&
                item.EmployeeId == employeeId.Trim() &&
                item.SessionId == request.SessionId.Trim(),
                cancellationToken);
        if (sessionEntity is null)
            return ApiResponse<EvaluationVerdictSyncResultDto>.ErrorResponse(404, "evaluation session not found");

        var verdict = request.Verdict;
        var verdictJson = JsonSerializer.Serialize(verdict, JsonOptions);
        await PersistTextAssetAsync(
            sessionEntity,
            assetType: "evaluator-verdict-json",
            relatedKey: $"verdict:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            fileName: $"evaluator_verdict_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
            content: verdictJson,
            mimeType: "application/json",
            sourceType: "frontend-ws",
            cancellationToken);

        var passed = string.Equals(verdict.Verdict?.Trim(), "PASS", StringComparison.OrdinalIgnoreCase);

        var reportResult = await UpsertReportAsync(
            employeeId,
            new EvaluationReportUpsertRequestDto
            {
                SessionId = request.SessionId.Trim(),
                OverallScore = verdict.OverallScore,
                Passed = passed,
                Summary = verdict.Summary,
                DimensionScores = verdict.DimensionScores
            },
            cancellationToken);

        if (!reportResult.Success || reportResult.Data is null)
            return ApiResponse<EvaluationVerdictSyncResultDto>.ErrorResponse(reportResult.Code, reportResult.Message);

        await UpdateSessionStatusAsync(sessionEntity, passed ? "passed" : "failed", null, cancellationToken);

        var updated = passed
            ? BuildAiPassResult(employee, verdict.Summary)
            : BuildAiFailResult(employee, verdict.Summary);
        await SaveEmployeeToDbAsync(updated, cancellationToken);

        var resultDto = new EvaluationVerdictSyncResultDto(
            employeeId.Trim(),
            request.SessionId.Trim(),
            passed,
            verdict.OverallScore,
            verdict.Summary ?? "",
            updated.Status ?? employee.Status ?? "interning_ai",
            new EvaluationReportSummaryDto(
                reportResult.Data.ReportId,
                reportResult.Data.Iteration,
                reportResult.Data.OverallScore,
                reportResult.Data.Passed,
                reportResult.Data.ReportJsonUrl,
                reportResult.Data.ReportHtmlUrl,
                DateTimeOffset.UtcNow.ToString("o"),
                verdict.DimensionScores));

        return ApiResponse<EvaluationVerdictSyncResultDto>.SuccessResponse(resultDto, "verdict synced and report persisted");
    }

    private static IReadOnlyList<EvaluationDimensionScoreDto> DeserializeDimensionScores(string? dimensionScoresJson)
    {
        if (string.IsNullOrWhiteSpace(dimensionScoresJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<EvaluationDimensionScoreDto>>(dimensionScoresJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 确保 evaluator 沙箱会话已启动，为后续材料上传对话和 WebSocket 连接做准备。
    /// </summary>
    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureEvaluatorConversationStartedAsync(
        string owner,
        EvaluationWorkspaceContext ctx,
        CancellationToken cancellationToken)
    {
        var sessionResult = await EnsureSandboxConversationStartedAsync(
            owner,
            ctx.EvaluatorHireId,
            ctx.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);

        if (!sessionResult.Success)
        {
            logger.LogError(
                "[Eval] Failed to start evaluator conversation sandboxId={SandboxId} code={Code} msg={Message}",
                ctx.EvaluatorSandboxId, sessionResult.Code, sessionResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(sessionResult.Code, sessionResult.Message);
        }

        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(ctx, "evaluator conversation started");
    }

    /// <summary>
    /// 准备补充材料对话频道。材料上传已改为通过 API 直接写入 workspace，此方法作为兼容层直通返回 ctx。
    /// </summary>
    private Task<ApiResponse<EvaluationWorkspaceContext>> EnsureSupplementConversationPreparedAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext ctx,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(ctx, "supplement conversation prepared"));
    }

    /// <summary>
    /// 检查 evaluator 当前是否具备完整的测试材料（testcases + ontology）。
    /// 若能从 workspace context + template 加载材料，则直接返回已就绪状态；否则返回缺失状态。
    /// </summary>
    private async Task<EvaluationReadinessDto> PrimeReadinessMaterialsAsync(
        string owner,
        string employeeId,
        CancellationToken cancellationToken)
    {
        var ctx = await LoadWorkspaceContextAsync(owner, employeeId, cancellationToken);
        if (ctx is null)
            return BuildReadiness(false, false);

        var employee = await GetEmployeeFromDbAsync(owner, employeeId, cancellationToken);
        if (employee is null)
            return BuildReadiness(false, false);

        var testcaseSources = await LoadTestcaseSourcesAsync(ctx, employee, cancellationToken);
        var ontologyProfile = await BuildOntologyProfileAsync(ctx, employee, cancellationToken);

        var testcaseReady = testcaseSources.Count > 0;
        // 有明确维度权重或规则文件时认为 ontology 就绪
        var ontologyReady = ontologyProfile.Sources.Count > 0 || ontologyProfile.DimensionWeights.Count > 0;

        return BuildReadiness(testcaseReady, ontologyReady);
    }

    /// <summary>
    /// 清理指定员工的所有评估数据（工作区状态、会话、资产、报告），供测试时重置评估流程使用。
    /// </summary>
    public async Task<ApiResponse<object>> ResetEvaluationDataAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return ApiResponse<object>.ErrorResponse(400, "employeeId cannot be empty");

        var accessContext = await ResolveEvaluationAccessContextAsync(employeeId, cancellationToken);
        if (accessContext is null)
            return ApiResponse<object>.ErrorResponse(404, "employee not found");

        var persistenceScope = accessContext.PersistenceScope;
        // 沙箱创建时使用 RequestOwner，删除时需保持一致
        var sandboxOwner = accessContext.RequestOwner;
        var normalizedEmployeeId = employeeId.Trim();

        // 先读取工作区状态，拿到需要删除的沙箱 ID
        var workspaceState = await dbContext.EvaluationWorkspaceStates
            .FirstOrDefaultAsync(
                item => item.OwnerSubject == persistenceScope && item.EmployeeId == normalizedEmployeeId,
                cancellationToken);

        var workspaceCtx = workspaceState is not null && !string.IsNullOrWhiteSpace(workspaceState.PayloadJson)
            ? JsonSerializer.Deserialize<EvaluationWorkspaceContext>(workspaceState.PayloadJson, JsonOptions)
            : null;

        // 删除评估沙箱（调用 provisioner + 标记 Deleted）；失败时仅记录警告，不阻断清理
        var sandboxIdsToDelete = new[]
        {
            workspaceCtx?.TargetSandboxId,
            workspaceCtx?.EvaluatorSandboxId,
        }.Where(sandboxId => !string.IsNullOrWhiteSpace(sandboxId)).Select(sandboxId => sandboxId!).Distinct().ToArray();

        int deletedSandboxCount = 0;
        foreach (var sandboxId in sandboxIdsToDelete)
        {
            var deleteResult = await sandboxService.DeleteForOwnerAsync(sandboxId, sandboxOwner, cancellationToken);
            if (deleteResult.Success)
            {
                deletedSandboxCount++;
                logger.LogInformation("[Eval] Reset: deleted sandbox sandboxId={SandboxId}", sandboxId);
            }
            else
            {
                // 沙箱不存在或已删除，跳过，不中断整体清理
                logger.LogWarning("[Eval] Reset: failed to delete sandbox sandboxId={SandboxId} reason={Reason}",
                    sandboxId, deleteResult.Message);
            }
        }

        // 删除工作区状态记录
        if (workspaceState is not null)
        {
            dbContext.EvaluationWorkspaceStates.Remove(workspaceState);
        }

        // 删除所有评估会话（级联删除 Assets 和 Reports）
        var sessions = await dbContext.EvaluationSessions
            .Include(item => item.Assets)
            .Include(item => item.Reports)
            .Where(item => item.OwnerSubject == persistenceScope && item.EmployeeId == normalizedEmployeeId)
            .ToListAsync(cancellationToken);
        if (sessions.Count > 0)
        {
            dbContext.EvaluationSessions.RemoveRange(sessions);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "[Eval] Reset evaluation data for employeeId={EmployeeId} scope={Scope}: " +
            "removedWorkspaceState={HasWorkspaceState}, deletedSandboxes={SandboxCount}, removedSessions={SessionCount}",
            normalizedEmployeeId, persistenceScope, workspaceState is not null, deletedSandboxCount, sessions.Count);

        return ApiResponse<object>.SuccessResponse(new
        {
            removedWorkspaceState = workspaceState is not null,
            deletedSandboxes = deletedSandboxCount,
            removedSessions = sessions.Count
        }, "评估数据已清理");
    }

}
