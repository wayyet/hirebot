using System.Collections.Concurrent;
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
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

internal sealed partial class EvaluationService(
    IEmployeeRuntimeStore store,
    IEmployeeHiringService employeeHiringService,
    IHiringArtifactPackageService artifactPackageService,
    ISandboxService sandboxService,
    IRequestContextService requestContextService,
    HireBotDbContext dbContext,
    IEvaluationAssetStore evaluationAssetStore,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILogger<EvaluationService> logger,
    ISystemSkillRegistry systemSkillRegistry,
    KingCrabSandboxTokenProvider sandboxTokenProvider) : IEvaluationService
{
    private static readonly string[] EvaluationSkillNames =
    [
        "evaluation_orchestrator",
        "scenario_parser",
        "test_executor",
        "evaluator",
        "training_advisor",
        "report_generator",
        "live_evaluation_coordinator"
    ];

    private static readonly ConcurrentDictionary<string, EvaluationWorkspaceContext> EvaluationWorkspaces =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string> TargetHireBindings =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> TargetArtifactPrimed =
        new(StringComparer.OrdinalIgnoreCase);

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
        ResolveEvaluationResourceRoot(hostEnvironment.ContentRootPath, configuration["HireBot:EvaluationResourceRoot"]);

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

    public async Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(400, "employeeId cannot be empty");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var normalizedEmployeeId = employeeId.Trim();
        var employee = await store.GetAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(404, "employee not found");
        }

        var normalizedEmployeeStatus = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired";
        var normalizedEvalPhase = employee.EvalPhase?.Trim().ToLowerInvariant();
        var shouldHideHistoricalSessionState =
            string.Equals(normalizedEmployeeStatus, "hired", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(normalizedEvalPhase) ||
            string.Equals(normalizedEvalPhase, "pending_skill_upload", StringComparison.OrdinalIgnoreCase);
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
                AssetRefs: null);
            return ApiResponse<EvaluationStateDto>.SuccessResponse(initialState);
        }

        var latestSession = await dbContext.EvaluationSessions
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == owner &&
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
                    CreatedAtUtc: reportEntity.CreatedAtUtc.ToString("o"));
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
            AssetRefs: assetRefs);

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

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            null,
            allowTargetHireCreation: true,
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
        EvaluationWorkspaces[BuildWorkspaceKey(owner, employee.EmployeeId)] = refreshedWorkspace;

        var questionCards = await LoadQuestionCardsForLatestSessionAsync(owner, employee.EmployeeId, cancellationToken);

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

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            request.SkillRootPath,
            null,
            allowTargetHireCreation: true,
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

        var sendRequest = new HiringConversationMessageRequestDto
        {
            Content = request.Content.Trim(),
            StructuredAnswers = request.StructuredAnswers,
            Materials = request.Materials
        };
        var sendResult = await SendSandboxMessageAsync(
            owner,
            workspaceResult.Data.EvaluatorHireId,
            workspaceResult.Data.EvaluatorSandboxId,
            "evaluation-evaluator",
            sendRequest,
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        var timelineResult = await GetSandboxTimelineAsync(
            owner,
            workspaceResult.Data.EvaluatorHireId,
            workspaceResult.Data.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        var refreshedWorkspace = workspaceResult.Data with { SessionId = timelineResult.Data.SessionId };
        EvaluationWorkspaces[BuildWorkspaceKey(owner, employee.EmployeeId)] = refreshedWorkspace;

        var questionCards = await LoadQuestionCardsForLatestSessionAsync(owner, employee.EmployeeId, cancellationToken);

        return ApiResponse<EvaluationSandboxConversationStateDto>.SuccessResponse(
            BuildSandboxConversationState(employee, refreshedWorkspace, timelineResult.Data, questionCards),
            "evaluation sandbox replied");
    }

    public async Task<ApiResponse<EmployeeDetailDto>> SubmitAiEvaluationDecisionAsync(
        string employeeId,
        AiEvaluationDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null || string.IsNullOrWhiteSpace(request.Decision))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId and decision are required");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "employee not found");
        }

        var decision = request.Decision.Trim().ToUpperInvariant();
        var currentStatus = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired";

        EmployeeDetailDto? updated = null;
        var message = "AI evaluation decision submitted";
        logger.LogInformation("[Eval] AiDecision employeeId={EmployeeId} decision={Decision} status={Status} phase={Phase}",
            employee.EmployeeId, decision, currentStatus, employee.EvalPhase);

        switch (decision)
        {
            case "START":
            {
                if (currentStatus is not ("hired" or "failed" or "interning_ai"))
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, $"current status does not allow START: {currentStatus}");
                }

                var startSkillRootPath = ExtractPathFromComment(request.Comment);
                var startWorkspaceResult = await EnsureWorkspaceReadyAsync(
                    owner,
                    employee,
                    startSkillRootPath,
                    request.Comment,
                    allowTargetHireCreation: true,
                    forceTargetHireRecreate: false,
                    cancellationToken);
                if (!startWorkspaceResult.Success || startWorkspaceResult.Data is null)
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(startWorkspaceResult.Code, startWorkspaceResult.Message);
                }

                var startWorkspaceContext = startWorkspaceResult.Data;
                await StartNewEvaluationSessionAsync(owner, employee.EmployeeId, startWorkspaceContext, cancellationToken);
                var startReadiness = await PrimeReadinessMaterialsAsync(employee.EmployeeId, cancellationToken);
                var startMaterialsReady = startReadiness.TestcasesReady && startReadiness.OntologyReady;

                var startCapabilities = MergeEvaluationCapabilities(employee.Capabilities, EvaluationSkillNames);
                var startConfigured = startCapabilities.Count > 0 && startCapabilities.All(item => item.Ready);

                updated = employee with
                {
                    Status = "interning_ai",
                    LifecycleStatus = "AI evaluation",
                    EvalPhase = "ai_running",
                    StageSummary = startMaterialsReady
                        ? "Evaluation skill loaded and materials are ready for auto-run."
                        : "Evaluation skill loaded but materials are incomplete. Waiting for testcase/ontology supplements.",
                    PrimarySignal = startMaterialsReady
                        ? "Double-sandbox evaluation environment is ready"
                        : "Testcase or ontology is missing, open supplement conversation to upload materials",
                    SignalLevel = startMaterialsReady ? "ok" : "warn",
                    PendingActions = startMaterialsReady
                        ? ["Run evaluation scenarios in evaluator sandbox"]
                        : ["Open supplement conversation and upload missing testcase/ontology materials"],
                    Capabilities = startCapabilities,
                    IsConfigured = startConfigured
                };
                message = "AI evaluation started, workspace and materials primed";
                logger.LogInformation("[Eval] START completed employeeId={EmployeeId} materialsReady={Ready}",
                    employee.EmployeeId, startMaterialsReady);
                break;
            }

            case "LOAD_SKILL":
            {
                if (currentStatus != "interning_ai")
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "LOAD_SKILL is only allowed in interning_ai status");
                }

                var skillRootPath = ExtractPathFromComment(request.Comment);
                var workspaceResult = await EnsureWorkspaceReadyAsync(
                    owner,
                    employee,
                    skillRootPath,
                    request.Comment,
                    allowTargetHireCreation: true,
                    forceTargetHireRecreate: false,
                    cancellationToken);
                if (!workspaceResult.Success || workspaceResult.Data is null)
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
                }

                var workspaceContext = workspaceResult.Data;
                await StartNewEvaluationSessionAsync(owner, employee.EmployeeId, workspaceContext, cancellationToken);
                var readiness = await PrimeReadinessMaterialsAsync(employee.EmployeeId, cancellationToken);
                var materialsReady = readiness.TestcasesReady && readiness.OntologyReady;

                var capabilities = MergeEvaluationCapabilities(employee.Capabilities, EvaluationSkillNames);
                var configured = capabilities.Count > 0 && capabilities.All(item => item.Ready);

                updated = employee with
                {
                    Status = "interning_ai",
                    LifecycleStatus = "AI evaluation",
                    EvalPhase = "ai_running",
                    StageSummary = materialsReady
                        ? "Evaluation skill loaded and materials are ready for auto-run."
                        : "Evaluation skill loaded but materials are incomplete. Waiting for testcase/ontology supplements.",
                    PrimarySignal = materialsReady
                        ? "Double-sandbox evaluation environment is ready"
                        : "Testcase or ontology is missing, open supplement conversation to upload materials",
                    SignalLevel = materialsReady ? "ok" : "warn",
                    PendingActions = materialsReady
                        ? ["Run evaluation scenarios in evaluator sandbox"]
                        : ["Open supplement conversation and upload missing testcase/ontology materials"],
                    Capabilities = capabilities,
                    IsConfigured = configured
                };
                message = "Evaluation skill loaded and evaluator workspace is ready";
                logger.LogInformation("[Eval] LOAD_SKILL completed employeeId={EmployeeId} materialsReady={Ready}",
                    employee.EmployeeId, materialsReady);
                break;
            }

            case "RUN":
            {
                if (currentStatus != "interning_ai")
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "RUN is only allowed in interning_ai status");
                }

                // Ensure workspace is ready (sandboxes + skill upload)
                var workspaceResult = await EnsureWorkspaceReadyAsync(
                    owner,
                    employee,
                    null,
                    request.Comment,
                    allowTargetHireCreation: true,
                    forceTargetHireRecreate: false,
                    cancellationToken);
                if (!workspaceResult.Success || workspaceResult.Data is null)
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
                }

                // Prime readiness materials (ensure testcases/ontology are loaded in target sandbox)
                await PrimeReadinessMaterialsAsync(employee.EmployeeId, cancellationToken);

                // Frontend will use WS to connect to evaluator sandbox for scoring.
                // Scoring result comes back via sync-verdict endpoint.
                updated = employee with
                {
                    EvalPhase = "ai_running",
                    StageSummary = "Evaluation workspace ready. Waiting for web client to connect via WebSocket for scoring.",
                    PrimarySignal = "Awaiting frontend WebSocket evaluation",
                    SignalLevel = "ok",
                    PendingActions = ["Connect to evaluator sandbox via WebSocket and run evaluation"]
                };
                message = "AI evaluation workspace ready for WebSocket scoring";
                logger.LogInformation("[Eval] RUN workspace ready employeeId={EmployeeId} awaiting WS evaluation",
                    employee.EmployeeId);
                break;
            }

            case "PASS":
            case "FAIL":
                return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "manual PASS/FAIL is disabled, use RUN to get sandbox verdict");
        }

        if (updated is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "decision only supports START, LOAD_SKILL, RUN, PASS, FAIL");
        }

        await store.UpsertAsync(owner, updated, cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, message);
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
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
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

        await store.UpsertAsync(owner, updated, cancellationToken);
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
        var employee = await store.GetAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            null,
            allowTargetHireCreation: true,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceResult.Data, cancellationToken);
        var warmupResult = await EnsureTargetArtifactBundleLoadedAsync(
            owner,
            employee,
            workspaceResult.Data,
            sessionEntity,
            forceRefresh: false,
            explicitArtifactPath: null,
            cancellationToken);
        if (!warmupResult.Success)
        {
            logger.LogInformation("Artifact warmup skipped for testcase loading (no package). Code={Code}", warmupResult.Code);
        }

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
        var employee = await store.GetAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationOntologyQueryResultDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            null,
            allowTargetHireCreation: true,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationOntologyQueryResultDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceResult.Data, cancellationToken);
        var warmupResult = await EnsureTargetArtifactBundleLoadedAsync(
            owner,
            employee,
            workspaceResult.Data,
            sessionEntity,
            forceRefresh: false,
            explicitArtifactPath: null,
            cancellationToken);
        if (!warmupResult.Success)
        {
            logger.LogInformation("Artifact warmup skipped for ontology loading (no package). Code={Code}", warmupResult.Code);
        }

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

    private async Task<ApiResponse<EvaluationTargetExecuteResultDto>> ExecuteTargetAsync(
        string employeeId,
        EvaluationTargetExecuteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) ||
            request is null ||
            string.IsNullOrWhiteSpace(request.TestcaseId) ||
            string.IsNullOrWhiteSpace(request.Input))
        {
            return ApiResponse<EvaluationTargetExecuteResultDto>.ErrorResponse(400, "employeeId, testcaseId and input are required");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var normalizedEmployeeId = employeeId.Trim();
        var employee = await store.GetAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationTargetExecuteResultDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            null,
            allowTargetHireCreation: true,
            forceTargetHireRecreate: false,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationTargetExecuteResultDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceResult.Data, cancellationToken);
        var testcaseId = request.TestcaseId.Trim();
        var input = request.Input.Trim();
        var startedAtUtc = DateTimeOffset.UtcNow;

        var warmupResult = await EnsureTargetArtifactBundleLoadedAsync(
            owner,
            employee,
            workspaceResult.Data,
            sessionEntity,
            forceRefresh: false,
            explicitArtifactPath: null,
            cancellationToken);
        if (!warmupResult.Success)
        {
            logger.LogInformation("Artifact warmup skipped for target execute (no package). Code={Code}", warmupResult.Code);
        }

        var startConversationResult = await EnsureSandboxConversationStartedAsync(
            owner,
            workspaceResult.Data.TargetHireId,
            workspaceResult.Data.TargetSandboxId,
            "evaluation-target",
            cancellationToken);
        if (!startConversationResult.Success && startConversationResult.Code != 409)
        {
            logger.LogInformation(
                "Target conversation start skipped. EmployeeId={EmployeeId}, TargetHireId={TargetHireId}, Code={Code}, Message={Message}",
                normalizedEmployeeId,
                workspaceResult.Data.TargetHireId,
                startConversationResult.Code,
                startConversationResult.Message);
        }

        var sendResult = await SendSandboxMessageAsync(
            owner,
            workspaceResult.Data.TargetHireId,
            workspaceResult.Data.TargetSandboxId,
            "evaluation-target",
            new HiringConversationMessageRequestDto
            {
                Content = BuildTargetExecutionPrompt(testcaseId, input)
            },
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            await UpdateSessionStatusAsync(sessionEntity, "execute_failed", sendResult.Message, cancellationToken);
            return ApiResponse<EvaluationTargetExecuteResultDto>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        var timelineResult = await GetSandboxTimelineAsync(
            owner,
            workspaceResult.Data.TargetHireId,
            workspaceResult.Data.TargetSandboxId,
            "evaluation-target",
            cancellationToken);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var executionId = $"exec_{Guid.NewGuid():N}";

        var tracePayload = new
        {
            executionId,
            sessionId = sessionEntity.SessionId,
            employeeId = normalizedEmployeeId,
            targetHireId = workspaceResult.Data.TargetHireId,
            targetSandboxId = workspaceResult.Data.TargetSandboxId,
            testcaseId,
            input,
            startedAtUtc = startedAtUtc.ToString("o"),
            completedAtUtc = completedAtUtc.ToString("o"),
            assistantMessage = sendResult.Data.AssistantMessage,
            timelineMessages = timelineResult.Success && timelineResult.Data is not null
                ? timelineResult.Data.Messages
                : Array.Empty<HiringConversationMessageDto>()
        };
        var traceJson = JsonSerializer.Serialize(tracePayload, JsonOptions);
        await PersistTextAssetAsync(
            sessionEntity,
            assetType: "trace-json",
            relatedKey: $"execution:{executionId}",
            fileName: $"trace_{testcaseId}_{executionId}.json",
            content: traceJson,
            mimeType: "application/json",
            sourceType: "target-sandbox",
            cancellationToken);
        await UpdateSessionStatusAsync(sessionEntity, "target_executed", null, cancellationToken);

        var result = new EvaluationTargetExecuteResultDto(
            SessionId: sessionEntity.SessionId,
            ExecutionId: executionId,
            TestcaseId: testcaseId,
            Status: "completed",
            StartedAtUtc: startedAtUtc.ToString("o"),
            CompletedAtUtc: completedAtUtc.ToString("o"));

        return ApiResponse<EvaluationTargetExecuteResultDto>.SuccessResponse(result, "target execution captured");
    }

    private async Task<ApiResponse<EvaluationTraceReadResultDto>> ReadTraceAsync(
        string employeeId,
        EvaluationTraceReadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) ||
            request is null ||
            string.IsNullOrWhiteSpace(request.ExecutionId) ||
            string.IsNullOrWhiteSpace(request.TestcaseId))
        {
            return ApiResponse<EvaluationTraceReadResultDto>.ErrorResponse(400, "employeeId, executionId and testcaseId are required");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var normalizedEmployeeId = employeeId.Trim();
        var employee = await store.GetAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationTraceReadResultDto>.ErrorResponse(404, "employee not found");
        }

        var executionId = request.ExecutionId.Trim();
        var traceRecord = await dbContext.EvaluationAssets
            .AsNoTracking()
            .Join(
                dbContext.EvaluationSessions.AsNoTracking(),
                asset => asset.SessionEntityId,
                session => session.Id,
                (asset, session) => new { Asset = asset, Session = session })
            .Where(item =>
                item.Session.OwnerSubject == owner &&
                item.Session.EmployeeId == normalizedEmployeeId &&
                item.Asset.AssetType == "trace-json" &&
                item.Asset.RelatedKey == $"execution:{executionId}")
            .OrderByDescending(item => item.Asset.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (traceRecord is null)
        {
            return ApiResponse<EvaluationTraceReadResultDto>.ErrorResponse(404, "trace asset not found");
        }

        var physicalPath = ResolvePhysicalAssetPath(traceRecord.Asset.RelativePath);
        if (string.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath))
        {
            return ApiResponse<EvaluationTraceReadResultDto>.ErrorResponse(404, "trace file missing on disk");
        }

        var traceJson = await File.ReadAllTextAsync(physicalPath, cancellationToken);
        var result = new EvaluationTraceReadResultDto(
            SessionId: traceRecord.Session.SessionId,
            ExecutionId: executionId,
            TestcaseId: request.TestcaseId.Trim(),
            TraceJson: traceJson,
            TraceAsset: ToAssetRef(traceRecord.Asset));
        return ApiResponse<EvaluationTraceReadResultDto>.SuccessResponse(result);
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
        var employee = await store.GetAsync(owner, normalizedEmployeeId, cancellationToken);
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

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(404, "employee not found");

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner, employee, null, null,
            allowTargetHireCreation: true,
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

        var token = await sandboxTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return ApiResponse<EvaluationSandboxConnectionResultDto>.ErrorResponse(502, "unable to acquire sandbox access token");

        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, ctx, cancellationToken);

        // Build evaluation payload: fetch testcases, ontology, execute target, read traces.
        // If full pipeline fails, fall back to a context-only payload so the evaluator
        // agent at least knows what testcases and ontology are available.
        string payloadJson;
        try
        {
            var testcaseResult = await FetchTestcasesAsync(employeeId, cancellationToken);
            var ontologyResult = await QueryOntologyAsync(employeeId, cancellationToken);

            var testcasesOk = testcaseResult.Success && testcaseResult.Data is not null &&
                              testcaseResult.Data.Testcases.Count > 0;
            var ontologyOk = ontologyResult.Success && ontologyResult.Data is not null;

            if (!testcasesOk || !ontologyOk)
            {
                // Build context-only payload so the evaluator agent knows what's missing
                var contextPayload = new
                {
                    session_id = sessionEntity.SessionId,
                    target_hire_id = ctx.TargetHireId,
                    status = "materials_incomplete",
                    testcases_available = testcasesOk,
                    ontology_available = ontologyOk,
                    instruction = "Evaluation materials are not yet complete. Ask the user to upload missing testcases or ontology files before running evaluation."
                };
                payloadJson = JsonSerializer.Serialize(contextPayload, JsonOptions);
                logger.LogWarning("[Eval] Context-only payload employeeId={EmployeeId} testcasesOk={TC} ontologyOk={Ont}",
                    employeeId, testcasesOk, ontologyOk);
            }
            else
            {
                var executionEvidences = new List<TraceExecutionEvidence>(testcaseResult.Data.Testcases.Count);
                foreach (var testcase in testcaseResult.Data.Testcases)
                {
                    var executionInput = TryReadUserRequestFromRawTestcase(testcase.RawJson) ?? testcase.ScenarioName;
                    var executeResult = await ExecuteTargetAsync(
                        employeeId,
                        new EvaluationTargetExecuteRequestDto
                        {
                            TestcaseId = testcase.TestcaseId,
                            Input = executionInput
                        },
                        cancellationToken);
                    if (!executeResult.Success || executeResult.Data is null) continue;

                    var traceResult = await ReadTraceAsync(
                        employeeId,
                        new EvaluationTraceReadRequestDto
                        {
                            ExecutionId = executeResult.Data.ExecutionId,
                            TestcaseId = testcase.TestcaseId
                        },
                        cancellationToken);
                    if (!traceResult.Success || traceResult.Data is null) continue;

                    executionEvidences.Add(new TraceExecutionEvidence(
                        TestcaseId: testcase.TestcaseId,
                        ScenarioName: testcase.ScenarioName,
                        Input: executionInput,
                        ExecutionId: executeResult.Data.ExecutionId,
                        TraceJson: traceResult.Data.TraceJson,
                        TraceAssetUrl: traceResult.Data.TraceAsset.PublicUrl));
                }

                var payload = BuildEvaluatorPayload(
                    sessionEntity.SessionId, testcaseResult.Data, ontologyResult.Data, executionEvidences);
                payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                logger.LogInformation("[Eval] Full payload built employeeId={EmployeeId} testcases={Count} evidences={EvCount}",
                    employeeId, testcaseResult.Data.Testcases.Count, executionEvidences.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Eval] Failed to build evaluation payload employeeId={EmployeeId}", employeeId);
            var errorPayload = new
            {
                session_id = sessionEntity.SessionId,
                target_hire_id = ctx.TargetHireId,
                status = "payload_error",
                error = ex.Message,
                instruction = "Failed to build evaluation payload. Please retry or contact the administrator."
            };
            payloadJson = JsonSerializer.Serialize(errorPayload, JsonOptions);
        }

        await UpdateSessionStatusAsync(sessionEntity, "ws_connected", null, cancellationToken);

        var result = new EvaluationSandboxConnectionResultDto(
            gatewayEndpoint,
            token,
            ctx.EvaluatorSandboxId,
            sessionEntity.SessionId,
            ctx.TargetHireId,
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

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
            return ApiResponse<EvaluationVerdictSyncResultDto>.ErrorResponse(404, "employee not found");

        var sessionEntity = await dbContext.EvaluationSessions
            .FirstOrDefaultAsync(item =>
                item.OwnerSubject == owner &&
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
        await store.UpsertAsync(owner, updated, cancellationToken);

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
                DateTimeOffset.UtcNow.ToString("o")));

        return ApiResponse<EvaluationVerdictSyncResultDto>.SuccessResponse(resultDto, "verdict synced and report persisted");
    }

}
