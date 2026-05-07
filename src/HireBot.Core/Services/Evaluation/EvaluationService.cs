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
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

internal sealed class EvaluationService(
    IEmployeeRuntimeStore store,
    IEmployeeHiringService employeeHiringService,
    IHiringArtifactPackageService artifactPackageService,
    ISandboxService sandboxService,
    IRequestContextService requestContextService,
    HireBotDbContext dbContext,
    IEvaluationAssetStore evaluationAssetStore,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILogger<EvaluationService> logger) : IEvaluationService
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
            case "SKILL_UPLOADED":
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

                if (!string.Equals(employee.EvalPhase, "ai_running", StringComparison.OrdinalIgnoreCase))
                {
                    var chainSkillRootPath = ExtractPathFromComment(request.Comment);
                    var chainWorkspaceResult = await EnsureWorkspaceReadyAsync(
                        owner,
                        employee,
                        chainSkillRootPath,
                        request.Comment,
                        allowTargetHireCreation: true,
                        forceTargetHireRecreate: false,
                        cancellationToken);
                    if (!chainWorkspaceResult.Success || chainWorkspaceResult.Data is null)
                    {
                        return ApiResponse<EmployeeDetailDto>.ErrorResponse(chainWorkspaceResult.Code, chainWorkspaceResult.Message);
                    }

                    await StartNewEvaluationSessionAsync(owner, employee.EmployeeId, chainWorkspaceResult.Data, cancellationToken);
                    var chainReadiness = await PrimeReadinessMaterialsAsync(employee.EmployeeId, cancellationToken);

                    if (!chainReadiness.TestcasesReady || !chainReadiness.OntologyReady)
                    {
                        var missingDetail = !chainReadiness.TestcasesReady && !chainReadiness.OntologyReady
                            ? "testcases and ontology"
                            : !chainReadiness.TestcasesReady
                                ? "testcases"
                                : "ontology";

                        return ApiResponse<EmployeeDetailDto>.ErrorResponse(422,
                            $"Cannot run evaluation: {missingDetail} are missing. Place required files in the target sandbox artifact package, then retry.");
                    }
                }

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

                var verdictResult = await RunAiEvaluationAsync(employee, workspaceResult.Data, cancellationToken);
                if (!verdictResult.Success || verdictResult.Data is null)
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(verdictResult.Code, verdictResult.Message);
                }

                updated = verdictResult.Data.Passed
                    ? BuildAiPassResult(employee, verdictResult.Data.Summary)
                    : BuildAiFailResult(employee, verdictResult.Data.Summary);
                message = verdictResult.Data.Passed
                    ? "AI evaluation completed by evaluator sandbox: PASS"
                    : "AI evaluation completed by evaluator sandbox: FAIL";
                logger.LogInformation("[Eval] RUN completed employeeId={EmployeeId} passed={Passed} score={Score}",
                    employee.EmployeeId, verdictResult.Data.Passed, verdictResult.Data.OverallScore);
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

    public async Task<ApiResponse<EvaluationFetchTestcasesResultDto>> FetchTestcasesAsync(
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
            await UpdateSessionStatusAsync(sessionEntity, "waiting_materials", warmupResult.Message, cancellationToken);
            return ApiResponse<EvaluationFetchTestcasesResultDto>.ErrorResponse(warmupResult.Code, warmupResult.Message);
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

    public async Task<ApiResponse<EvaluationOntologyQueryResultDto>> QueryOntologyAsync(
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
            await UpdateSessionStatusAsync(sessionEntity, "waiting_materials", warmupResult.Message, cancellationToken);
            return ApiResponse<EvaluationOntologyQueryResultDto>.ErrorResponse(warmupResult.Code, warmupResult.Message);
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

    public async Task<ApiResponse<EvaluationTargetBootstrapResultDto>> BootstrapTargetSandboxAsync(
        string employeeId,
        EvaluationTargetBootstrapRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationTargetBootstrapResultDto>.ErrorResponse(400, "employeeId cannot be empty");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var normalizedEmployeeId = employeeId.Trim();
        var employee = await store.GetAsync(owner, normalizedEmployeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationTargetBootstrapResultDto>.ErrorResponse(404, "employee not found");
        }

        var workspaceResult = await EnsureWorkspaceReadyAsync(
            owner,
            employee,
            null,
            null,
            allowTargetHireCreation: true,
            forceTargetHireRecreate: request?.ForceRecreate == true,
            cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationTargetBootstrapResultDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceResult.Data, cancellationToken);
        var warmupResult = await EnsureTargetArtifactBundleLoadedAsync(
            owner,
            employee,
            workspaceResult.Data,
            sessionEntity,
            forceRefresh: request?.ForceRecreate == true,
            explicitArtifactPath: request?.SourceArtifactPath,
            cancellationToken: cancellationToken);
        if (!warmupResult.Success || warmupResult.Data is null)
        {
            return ApiResponse<EvaluationTargetBootstrapResultDto>.ErrorResponse(warmupResult.Code, warmupResult.Message);
        }

        var result = new EvaluationTargetBootstrapResultDto(
            EmployeeId: normalizedEmployeeId,
            BackendId: "hiring-conversation",
            TargetRuntimeId: workspaceResult.Data.TargetHireId,
            EvaluatorRuntimeId: workspaceResult.Data.EvaluatorHireId,
            SessionId: sessionEntity.SessionId,
            WorkspacePath: warmupResult.Data.WorkspacePath,
            SourceArtifactPath: warmupResult.Data.SourceArtifactPath,
            StartedAtUtc: DateTimeOffset.UtcNow.ToString("o"));

        return ApiResponse<EvaluationTargetBootstrapResultDto>.SuccessResponse(result, "target sandbox artifact warmup completed");
    }

    public async Task<ApiResponse<EvaluationTargetExecuteResultDto>> ExecuteTargetAsync(
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
            await UpdateSessionStatusAsync(sessionEntity, "execute_failed", warmupResult.Message, cancellationToken);
            return ApiResponse<EvaluationTargetExecuteResultDto>.ErrorResponse(warmupResult.Code, warmupResult.Message);
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

    public async Task<ApiResponse<EvaluationTraceReadResultDto>> ReadTraceAsync(
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

    public async Task<ApiResponse<EvaluationReportUpsertResultDto>> UpsertReportAsync(
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

    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureWorkspaceReadyAsync(
        string owner,
        EmployeeDetailDto employee,
        string? skillRootPath,
        string? comment,
        bool allowTargetHireCreation,
        bool forceTargetHireRecreate,
        CancellationToken cancellationToken)
    {
        var workspaceKey = BuildWorkspaceKey(owner, employee.EmployeeId);
        var explicitTargetHireId = ExtractTargetRuntimeIdFromComment(comment);
        var hasBoundTargetHireId = false;
        string? targetHireId = null;
        if (!forceTargetHireRecreate)
        {
            targetHireId = explicitTargetHireId;
            if (string.IsNullOrWhiteSpace(targetHireId) &&
                TargetHireBindings.TryGetValue(workspaceKey, out var boundTargetHireId))
            {
                targetHireId = boundTargetHireId;
                hasBoundTargetHireId = true;
            }

        }

        if (string.IsNullOrWhiteSpace(targetHireId) && allowTargetHireCreation)
        {
            var createTargetResult = await CreateTargetHireAsync(owner, employee, comment, cancellationToken);
            if (!createTargetResult.Success || string.IsNullOrWhiteSpace(createTargetResult.Data))
            {
                return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(createTargetResult.Code, createTargetResult.Message);
            }

            targetHireId = createTargetResult.Data.Trim();
        }

        if (string.IsNullOrWhiteSpace(targetHireId))
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(422, "cannot resolve target runtimeId");
        }

        var targetStatusResult = await employeeHiringService.GetHiringStatusAsync(targetHireId, cancellationToken);
        if (!targetStatusResult.Success || targetStatusResult.Data is null)
        {
            var shouldRetryWithNewTargetHire =
                allowTargetHireCreation &&
                !forceTargetHireRecreate &&
                string.IsNullOrWhiteSpace(explicitTargetHireId) &&
                !hasBoundTargetHireId;

            if (shouldRetryWithNewTargetHire)
            {
                var createTargetResult = await CreateTargetHireAsync(owner, employee, comment, cancellationToken);
                if (!createTargetResult.Success || string.IsNullOrWhiteSpace(createTargetResult.Data))
                {
                    return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(createTargetResult.Code, createTargetResult.Message);
                }

                targetHireId = createTargetResult.Data.Trim();
                targetStatusResult = await employeeHiringService.GetHiringStatusAsync(targetHireId, cancellationToken);
            }
        }

        if (!targetStatusResult.Success || targetStatusResult.Data is null)
        {
            logger.LogWarning(
                "Failed to load target sandbox status from remote. Owner={Owner}, EmployeeId={EmployeeId}, TargetHireId={TargetHireId}, Code={Code}, Message={Message}",
                owner,
                employee.EmployeeId,
                targetHireId,
                targetStatusResult.Code,
                targetStatusResult.Message);

            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(
                targetStatusResult.Code,
                $"failed to read target sandbox info by runtimeId: {targetStatusResult.Message}");
        }

        if (string.IsNullOrWhiteSpace(targetStatusResult.Data.SandboxId))
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(422, "target sandbox info is incomplete: sandboxId missing");
        }

        TargetHireBindings[workspaceKey] = targetHireId;

        EvaluationWorkspaceContext workspaceContext;
        if (EvaluationWorkspaces.TryGetValue(workspaceKey, out var existingWorkspace) &&
            existingWorkspace.TargetHireId.Equals(targetHireId, StringComparison.OrdinalIgnoreCase))
        {
            workspaceContext = existingWorkspace with
            {
                TargetSandboxId = targetStatusResult.Data.SandboxId
            };
        }
        else
        {
            var createWorkspaceResult = await employeeHiringService.CreateEvaluationWorkspaceAsync(targetHireId, cancellationToken);
            if (!createWorkspaceResult.Success || createWorkspaceResult.Data is null)
            {
                return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(
                    createWorkspaceResult.Code,
                    $"failed to create evaluator workspace: {createWorkspaceResult.Message}");
            }

            workspaceContext = new EvaluationWorkspaceContext(
                TargetHireId: targetHireId,
                TargetSandboxId: targetStatusResult.Data.SandboxId,
                EvaluatorHireId: createWorkspaceResult.Data.HireId,
                EvaluatorSandboxId: createWorkspaceResult.Data.SandboxId,
                SkillLoadedAtUtc: null,
                SessionId: null);
        }

        if (workspaceContext.SkillLoadedAtUtc is null || !string.IsNullOrWhiteSpace(skillRootPath))
        {
            var uploadResult = await employeeHiringService.UploadEvaluationSkillAsync(
                workspaceContext.EvaluatorHireId,
                skillRootPath,
                cancellationToken);
            if (!uploadResult.Success || uploadResult.Data is not true)
            {
                return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(uploadResult.Code, uploadResult.Message);
            }

            workspaceContext = workspaceContext with
            {
                SkillLoadedAtUtc = DateTimeOffset.UtcNow
            };
        }

        EvaluationWorkspaces[workspaceKey] = workspaceContext;
        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext);
    }

    private async Task<ApiResponse<string>> CreateTargetHireAsync(
        string owner,
        EmployeeDetailDto employee,
        string? comment,
        CancellationToken cancellationToken)
    {
        var templateId = ExtractValueFromComment(comment, "templateId");
        if (string.IsNullOrWhiteSpace(templateId))
        {
            templateId = ResolveTargetTemplateId(employee);
        }

        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<string>.ErrorResponse(422, "cannot resolve target templateId");
        }

        var hireResult = await employeeHiringService.HireAsync(
            templateId,
            new HireTemplateRequestDto
            {
                UseCase = $"evaluation-target-for:{employee.EmployeeId}"
            },
            cancellationToken);
        if (!hireResult.Success || hireResult.Data is null || string.IsNullOrWhiteSpace(hireResult.Data.HireId))
        {
            return ApiResponse<string>.ErrorResponse(hireResult.Code, $"failed to create target sandbox: {hireResult.Message}");
        }

        logger.LogInformation(
            "Created target sandbox for evaluation. Owner={Owner}, EmployeeId={EmployeeId}, TemplateId={TemplateId}, TargetHireId={TargetHireId}",
            owner,
            employee.EmployeeId,
            templateId,
            hireResult.Data.HireId);

        return ApiResponse<string>.SuccessResponse(hireResult.Data.HireId);
    }

    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureEvaluatorConversationStartedAsync(
        string owner,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(workspaceContext.SessionId))
        {
            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext);
        }

        var startResult = await EnsureSandboxConversationStartedAsync(
            owner,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!startResult.Success || startResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(
                startResult.Code,
                $"failed to start evaluator conversation: {startResult.Message}");
        }

        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
        {
            SessionId = startResult.Data.SessionId
        });
    }

    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureSupplementConversationPreparedAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceContext, cancellationToken);

        var testcaseReady = await dbContext.EvaluationAssets
            .AsNoTracking()
            .AnyAsync(
                item => item.SessionEntityId == sessionEntity.Id && item.AssetType == "testcases-json",
                cancellationToken);
        var ontologyReady = await dbContext.EvaluationAssets
            .AsNoTracking()
            .AnyAsync(
                item => item.SessionEntityId == sessionEntity.Id && item.AssetType == "ontology-json",
                cancellationToken);
        if (testcaseReady && ontologyReady)
        {
            var readyTimelineResult = await GetSandboxTimelineAsync(
                owner,
                workspaceContext.EvaluatorHireId,
                workspaceContext.EvaluatorSandboxId,
                "evaluation-evaluator",
                cancellationToken);
            if (!readyTimelineResult.Success || readyTimelineResult.Data is null)
            {
                return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(readyTimelineResult.Code, readyTimelineResult.Message);
            }

            if (HasEvaluationReadyPrompt(readyTimelineResult.Data.Messages))
            {
                return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
                {
                    SessionId = readyTimelineResult.Data.SessionId
                });
            }

            var testcaseAssetCandidates = await dbContext.EvaluationAssets
                .AsNoTracking()
                .Where(item =>
                    item.SessionEntityId == sessionEntity.Id &&
                    item.AssetType == "testcases-json")
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var testcaseAssets = testcaseAssetCandidates
                .GroupBy(
                    item => string.IsNullOrWhiteSpace(item.RelatedKey) ? item.RelativePath : item.RelatedKey,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .First())
                .Take(5)
                .ToArray();
            var cards = await BuildQuestionCardsFromAssetsAsync(testcaseAssets, cancellationToken);
            var questionCardsMarkdown = BuildQuestionCardsMarkdown(cards);
            var ontologyRulesMarkdown = BuildOntologyRulesMarkdown();

            var readySendResult = await SendSandboxMessageAsync(
                owner,
                workspaceContext.EvaluatorHireId,
                workspaceContext.EvaluatorSandboxId,
                "evaluation-evaluator",
                new HiringConversationMessageRequestDto
                {
                    Content = "评估资料已就绪。你可以继续对话询问题卡细节、评分标准，或直接开始执行评估。",
                    StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["evaluation_context_ready"] = "true",
                        ["question_cards_markdown"] = questionCardsMarkdown,
                        ["question_cards_announced"] = "false",
                        ["ontology_rules_markdown"] = ontologyRulesMarkdown
                    }
                },
                cancellationToken);
            if (!readySendResult.Success || readySendResult.Data is null)
            {
                return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(readySendResult.Code, readySendResult.Message);
            }

            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
            {
                SessionId = readySendResult.Data.SessionId
            });
        }

        var timelineResult = await GetSandboxTimelineAsync(
            owner,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        if (HasMaterialsSupplementPrompt(timelineResult.Data.Messages))
        {
            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
            {
                SessionId = timelineResult.Data.SessionId
            });
        }

        var sendResult = await SendSandboxMessageAsync(
            owner,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            new HiringConversationMessageRequestDto
            {
                Content = "检测到评估资料不完整，请引导用户补充缺失素材（测试用例/评估本体），补充后继续执行评估流程。",
                StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["missing_materials"] = BuildMissingMaterialsSummary(testcaseReady, ontologyReady),
                    ["next_step"] = "请用户上传场景素材或回复场景描述，然后执行 scenario_parser 并重试评估。"
                }
            },
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
        {
            SessionId = sendResult.Data.SessionId
        });
    }

    private async Task<EvaluationReadinessDto> PrimeReadinessMaterialsAsync(
        string employeeId,
        CancellationToken cancellationToken)
    {
        var testcaseResult = await FetchTestcasesAsync(employeeId, cancellationToken);
        if (!testcaseResult.Success)
        {
            logger.LogInformation(
                "Testcase priming failed in LOAD_SKILL. EmployeeId={EmployeeId}, Code={Code}, Message={Message}",
                employeeId,
                testcaseResult.Code,
                testcaseResult.Message);
        }

        var ontologyResult = await QueryOntologyAsync(employeeId, cancellationToken);
        if (!ontologyResult.Success)
        {
            logger.LogInformation(
                "Ontology priming failed in LOAD_SKILL. EmployeeId={EmployeeId}, Code={Code}, Message={Message}",
                employeeId,
                ontologyResult.Code,
                ontologyResult.Message);
        }

        var testcaseReady = testcaseResult.Success &&
            testcaseResult.Data is not null &&
            testcaseResult.Data.Testcases.Count > 0;
        var ontologyReady = ontologyResult.Success &&
            ontologyResult.Data is not null &&
            (ontologyResult.Data.DimensionRules.Count > 0 || ontologyResult.Data.DimensionWeights.Count > 0);

        return BuildReadiness(testcaseReady, ontologyReady);
    }

    private async Task<ApiResponse<EvaluatorVerdictResult>> RunAiEvaluationPipelineAsync(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var owner = requestContextService.ResolveOwnerSubject();
        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceContext, cancellationToken);
        logger.LogInformation("[Eval] Pipeline start employeeId={EmployeeId} sessionId={SessionId}",
            employee.EmployeeId, sessionEntity.SessionId);

        var testcaseResult = await FetchTestcasesAsync(employee.EmployeeId, cancellationToken);
        if (!testcaseResult.Success || testcaseResult.Data is null)
        {
            await UpdateSessionStatusAsync(sessionEntity, "run_failed", testcaseResult.Message, cancellationToken);
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(testcaseResult.Code, testcaseResult.Message);
        }

        if (testcaseResult.Data.Testcases.Count == 0)
        {
            await UpdateSessionStatusAsync(sessionEntity, "run_failed", "no testcase available", cancellationToken);
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(422, "no testcase available");
        }

        var ontologyResult = await QueryOntologyAsync(employee.EmployeeId, cancellationToken);
        logger.LogInformation("[Eval] Testcases loaded employeeId={EmployeeId} count={Count}",
            employee.EmployeeId, testcaseResult.Data.Testcases.Count);
        if (!ontologyResult.Success || ontologyResult.Data is null)
        {
            await UpdateSessionStatusAsync(sessionEntity, "run_failed", ontologyResult.Message, cancellationToken);
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(ontologyResult.Code, ontologyResult.Message);
        }

        var executionEvidences = new List<TraceExecutionEvidence>(testcaseResult.Data.Testcases.Count);
        foreach (var testcase in testcaseResult.Data.Testcases)
        {
            var executionInput = TryReadUserRequestFromRawTestcase(testcase.RawJson) ?? testcase.ScenarioName;
            var executeResult = await ExecuteTargetAsync(
                employee.EmployeeId,
                new EvaluationTargetExecuteRequestDto
                {
                    TestcaseId = testcase.TestcaseId,
                    Input = executionInput
                },
                cancellationToken);
            if (!executeResult.Success || executeResult.Data is null)
            {
                await UpdateSessionStatusAsync(sessionEntity, "run_failed", executeResult.Message, cancellationToken);
                return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(executeResult.Code, executeResult.Message);
            }

            var traceResult = await ReadTraceAsync(
                employee.EmployeeId,
                new EvaluationTraceReadRequestDto
                {
                    ExecutionId = executeResult.Data.ExecutionId,
                    TestcaseId = testcase.TestcaseId
                },
                cancellationToken);
            if (!traceResult.Success || traceResult.Data is null)
            {
                await UpdateSessionStatusAsync(sessionEntity, "run_failed", traceResult.Message, cancellationToken);
                return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(traceResult.Code, traceResult.Message);
            }

            executionEvidences.Add(new TraceExecutionEvidence(
                TestcaseId: testcase.TestcaseId,
                ScenarioName: testcase.ScenarioName,
                Input: executionInput,
                ExecutionId: executeResult.Data.ExecutionId,
                TraceJson: traceResult.Data.TraceJson,
                TraceAssetUrl: traceResult.Data.TraceAsset.PublicUrl));
        }

        logger.LogInformation("[Eval] Execution completed employeeId={EmployeeId} evidenceCount={Count}",
            employee.EmployeeId, executionEvidences.Count);

        var evaluatorVerdictResult = await RequestSandboxVerdictAsync(
            employee,
            workspaceContext,
            sessionEntity,
            testcaseResult.Data,
            ontologyResult.Data,
            executionEvidences,
            cancellationToken);
        if (!evaluatorVerdictResult.Success || evaluatorVerdictResult.Data is null)
        {
            logger.LogWarning("[Eval] Verdict failed employeeId={EmployeeId} message={Message}",
                employee.EmployeeId, evaluatorVerdictResult.Message);
            await UpdateSessionStatusAsync(sessionEntity, "run_failed", evaluatorVerdictResult.Message, cancellationToken);
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(evaluatorVerdictResult.Code, evaluatorVerdictResult.Message);
        }

        var evaluatorVerdict = evaluatorVerdictResult.Data;
        logger.LogInformation("[Eval] Verdict received employeeId={EmployeeId} passed={Passed} score={Score}",
            employee.EmployeeId, evaluatorVerdict.Passed, evaluatorVerdict.OverallScore);
        await PersistTextAssetAsync(
            sessionEntity,
            assetType: "evaluator-verdict-json",
            relatedKey: $"verdict:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            fileName: $"evaluator_verdict_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
            content: evaluatorVerdict.RawVerdictJson,
            mimeType: "application/json",
            sourceType: "evaluator",
            cancellationToken);

        var reportResult = await UpsertReportAsync(
            employee.EmployeeId,
            new EvaluationReportUpsertRequestDto
            {
                SessionId = sessionEntity.SessionId,
                OverallScore = evaluatorVerdict.OverallScore,
                Passed = evaluatorVerdict.Passed,
                Summary = evaluatorVerdict.Summary,
                DimensionScores = evaluatorVerdict.DimensionScores
            },
            cancellationToken);
        if (!reportResult.Success || reportResult.Data is null)
        {
            await UpdateSessionStatusAsync(sessionEntity, "run_failed", reportResult.Message, cancellationToken);
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(reportResult.Code, reportResult.Message);
        }

        await UpdateSessionStatusAsync(sessionEntity, evaluatorVerdict.Passed ? "passed" : "failed", null, cancellationToken);
        logger.LogInformation("[Eval] Pipeline complete employeeId={EmployeeId} passed={Passed} score={Score} reportIteration={Iter}",
            employee.EmployeeId, evaluatorVerdict.Passed, evaluatorVerdict.OverallScore, reportResult.Data.Iteration);
        return ApiResponse<EvaluatorVerdictResult>.SuccessResponse(evaluatorVerdict);
    }

    private async Task<ApiResponse<EvaluatorVerdictResult>> RunAiEvaluationAsync(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        return await RunAiEvaluationPipelineAsync(employee, workspaceContext, cancellationToken);
    }

    private async Task<ApiResponse<EvaluatorVerdictResult>> RequestSandboxVerdictAsync(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        EvaluationFetchTestcasesResultDto testcaseData,
        EvaluationOntologyQueryResultDto ontologyData,
        IReadOnlyList<TraceExecutionEvidence> executionEvidences,
        CancellationToken cancellationToken)
    {
        var sessionResult = await EnsureEvaluatorConversationStartedAsync(employee.OwnerUserId, workspaceContext, cancellationToken);
        if (!sessionResult.Success || sessionResult.Data is null)
        {
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(sessionResult.Code, sessionResult.Message);
        }

        var payload = BuildEvaluatorPayload(sessionEntity.SessionId, testcaseData, ontologyData, executionEvidences);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var prompt = BuildEvaluatorPrompt(payloadJson);

        var sendResult = await SendSandboxMessageAsync(
            employee.OwnerUserId,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            new HiringConversationMessageRequestDto
            {
                Content = prompt,
                StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evaluation_mode"] = "run_scoring",
                    ["evaluation_payload_json"] = payloadJson,
                    ["evaluation_employee_id"] = employee.EmployeeId
                }
            },
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        var verdict = ParseSandboxVerdict(sendResult.Data.AssistantMessage.Content);
        if (verdict is not null)
        {
            return ApiResponse<EvaluatorVerdictResult>.SuccessResponse(verdict);
        }

        var timelineResult = await GetSandboxTimelineAsync(
            employee.OwnerUserId,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        var latestAssistantMessage = timelineResult.Data.Messages
            .LastOrDefault(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase));
        var parsedFromTimeline = ParseSandboxVerdict(latestAssistantMessage?.Content);
        if (parsedFromTimeline is not null)
        {
            return ApiResponse<EvaluatorVerdictResult>.SuccessResponse(parsedFromTimeline);
        }

        return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(422, "评估沙箱未返回可解析的评分结论 JSON");
    }

    private static object BuildEvaluatorPayload(
        string sessionId,
        EvaluationFetchTestcasesResultDto testcaseData,
        EvaluationOntologyQueryResultDto ontologyData,
        IReadOnlyList<TraceExecutionEvidence> executionEvidences)
    {
        return new
        {
            session_id = sessionId,
            target_hire_id = testcaseData.TargetHireId,
            testcase_count = testcaseData.Testcases.Count,
            question_cards = testcaseData.QuestionCards.Select(card => new
            {
                testcase_id = card.TestcaseId,
                title = card.Title,
                prompt = card.Prompt,
                scoring_hint = card.ScoringHint,
                steps = card.Steps
            }),
            ontology = new
            {
                dimension_weights = ontologyData.DimensionWeights,
                dimension_rules = ontologyData.DimensionRules
            },
            executions = executionEvidences.Select(item => new
            {
                testcase_id = item.TestcaseId,
                scenario_name = item.ScenarioName,
                input = item.Input,
                execution_id = item.ExecutionId,
                trace_json = item.TraceJson,
                trace_asset_url = item.TraceAssetUrl
            })
        };
    }

    private static string BuildEvaluatorPrompt(string payloadJson)
    {
        return string.Join(
            Environment.NewLine,
            [
                "你是评估沙箱中的 evaluator。",
                "请严格基于以下输入完成多维评分，并只返回 JSON（不要额外文本，不要 markdown 代码块）。",
                string.Empty,
                "输出 JSON schema:",
                "{",
                "  \"verdict\": \"PASS\" | \"FAIL\",",
                "  \"overall_score\": 0-100,",
                "  \"summary\": \"string\",",
                "  \"dimension_scores\": [",
                "    {",
                "      \"dimension\": \"accuracy|completeness|compliance|communication\",",
                "      \"score\": 0-100,",
                "      \"comment\": \"string\",",
                "      \"evidence_refs\": [\"trace-url-or-id\"]",
                "    }",
                "  ]",
                "}",
                string.Empty,
                "规则：",
                "1) 必须包含 4 个维度，每个维度都要有 evidence_refs。",
                "2) 没有证据就不能给高分，证据不足时应下调分数并在 comment 说明原因。",
                "3) overall_score 必须与维度分一致（可用加权平均）。",
                "4) verdict: overall_score >= 75 判定 PASS，否则 FAIL。",
                string.Empty,
                "输入数据：",
                payloadJson
            ]);
    }

    private static string StripThinkTags(string content)
    {
        var result = content;
        while (true)
        {
            var start = result.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            var end = result.IndexOf("</think>", start + 7, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;
            result = result[..start] + result[(end + 8)..];
        }
        return result;
    }

    private static EvaluatorVerdictResult? ParseSandboxVerdict(string? assistantContent)
    {
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            return null;
        }

        var trimmed = StripThinkTags(assistantContent).Trim();
        var jsonStart = trimmed.IndexOf('{');
        var jsonEnd = trimmed.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("verdict", out var verdictElement) &&
                    verdictElement.ValueKind == JsonValueKind.String)
                {
                    var verdictValue = verdictElement.GetString()?.Trim().ToUpperInvariant();
                    var passed = verdictValue is "PASS" or "PASSED" or "SUCCESS";
                    var failed = verdictValue is "FAIL" or "FAILED" or "REJECT";
                    if (passed || failed)
                    {
                        var summary = doc.RootElement.TryGetProperty("summary", out var summaryElement) &&
                                      summaryElement.ValueKind == JsonValueKind.String
                            ? summaryElement.GetString()
                            : null;

                        if (!doc.RootElement.TryGetProperty("overall_score", out var overallScoreElement) ||
                            overallScoreElement.ValueKind is not JsonValueKind.Number ||
                            !overallScoreElement.TryGetDecimal(out var overallScore))
                        {
                            return null;
                        }

                        if (!doc.RootElement.TryGetProperty("dimension_scores", out var dimensionScoresElement) ||
                            dimensionScoresElement.ValueKind is not JsonValueKind.Array)
                        {
                            return null;
                        }

                        var dimensionScores = new List<EvaluationDimensionScoreDto>();
                        foreach (var scoreElement in dimensionScoresElement.EnumerateArray())
                        {
                            if (scoreElement.ValueKind is not JsonValueKind.Object)
                            {
                                continue;
                            }

                            var dimension = scoreElement.TryGetProperty("dimension", out var dimensionElement) &&
                                            dimensionElement.ValueKind == JsonValueKind.String
                                ? dimensionElement.GetString()?.Trim()
                                : null;
                            var score = scoreElement.TryGetProperty("score", out var scoreValueElement) &&
                                        scoreValueElement.ValueKind is JsonValueKind.Number &&
                                        scoreValueElement.TryGetDecimal(out var decimalScore)
                                ? decimalScore
                                : -1m;
                            var comment = scoreElement.TryGetProperty("comment", out var commentElement) &&
                                          commentElement.ValueKind == JsonValueKind.String
                                ? commentElement.GetString()?.Trim()
                                : null;
                            if (string.IsNullOrWhiteSpace(dimension) ||
                                score < 0m || score > 100m ||
                                string.IsNullOrWhiteSpace(comment))
                            {
                                continue;
                            }

                            var evidenceRefs = new List<string>();
                            if (scoreElement.TryGetProperty("evidence_refs", out var evidenceElement) &&
                                evidenceElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var evidenceItem in evidenceElement.EnumerateArray())
                                {
                                    if (evidenceItem.ValueKind != JsonValueKind.String)
                                    {
                                        continue;
                                    }

                                    var value = evidenceItem.GetString();
                                    if (!string.IsNullOrWhiteSpace(value))
                                    {
                                        evidenceRefs.Add(value.Trim());
                                    }
                                }
                            }

                            if (evidenceRefs.Count == 0)
                            {
                                continue;
                            }

                            dimensionScores.Add(new EvaluationDimensionScoreDto(
                                Dimension: dimension,
                                Score: Math.Round(Math.Clamp(score, 0m, 100m), 2),
                                Comment: comment,
                                EvidenceRefs: evidenceRefs));
                        }

                        var requiredDimensions = new HashSet<string>(
                            ["accuracy", "completeness", "compliance", "communication"],
                            StringComparer.OrdinalIgnoreCase);
                        var coveredDimensions = dimensionScores
                            .Select(item => item.Dimension)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (!requiredDimensions.SetEquals(coveredDimensions))
                        {
                            return null;
                        }

                        if (overallScore < 0m || overallScore > 100m)
                        {
                            return null;
                        }

                        var normalizedSummary = string.IsNullOrWhiteSpace(summary)
                            ? (passed ? "评估沙箱判定通过。" : "评估沙箱判定未通过。")
                            : summary.Trim();

                        return new EvaluatorVerdictResult(
                            Passed: passed,
                            Summary: normalizedSummary,
                            OverallScore: Math.Round(Math.Clamp(overallScore, 0m, 100m), 2),
                            DimensionScores: dimensionScores,
                            RawVerdictJson: json);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private async Task StartNewEvaluationSessionAsync(
        string owner,
        string employeeId,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var latestIteration = await dbContext.EvaluationSessions
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == owner &&
                item.EmployeeId == employeeId)
            .Select(item => (int?)item.Iteration)
            .MaxAsync(cancellationToken) ?? 0;

        var session = new EvaluationSessionEntity
        {
            Id = Guid.NewGuid(),
            SessionId = BuildEvaluationSessionId(),
            OwnerSubject = owner,
            EmployeeId = employeeId,
            TargetHireId = workspaceContext.TargetHireId,
            TargetSandboxId = workspaceContext.TargetSandboxId,
            EvaluatorHireId = workspaceContext.EvaluatorHireId,
            EvaluatorSandboxId = workspaceContext.EvaluatorSandboxId,
            Status = "ready",
            Iteration = latestIteration + 1,
            LastError = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.EvaluationSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EvaluationSessionEntity> GetOrCreateSessionEntityAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var latestSession = await dbContext.EvaluationSessions
            .Where(item =>
                item.OwnerSubject == owner &&
                item.EmployeeId == employee.EmployeeId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSession is null ||
            !string.Equals(latestSession.TargetHireId, workspaceContext.TargetHireId, StringComparison.OrdinalIgnoreCase))
        {
            var created = new EvaluationSessionEntity
            {
                Id = Guid.NewGuid(),
                SessionId = BuildEvaluationSessionId(),
                OwnerSubject = owner,
                EmployeeId = employee.EmployeeId,
                TargetHireId = workspaceContext.TargetHireId,
                TargetSandboxId = workspaceContext.TargetSandboxId,
                EvaluatorHireId = workspaceContext.EvaluatorHireId,
                EvaluatorSandboxId = workspaceContext.EvaluatorSandboxId,
                Status = "ready",
                Iteration = 1,
                LastError = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.EvaluationSessions.Add(created);
            await dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }

        var changed = false;
        if (!string.Equals(latestSession.TargetSandboxId, workspaceContext.TargetSandboxId, StringComparison.OrdinalIgnoreCase))
        {
            latestSession.TargetSandboxId = workspaceContext.TargetSandboxId;
            changed = true;
        }

        if (!string.Equals(latestSession.EvaluatorHireId, workspaceContext.EvaluatorHireId, StringComparison.OrdinalIgnoreCase))
        {
            latestSession.EvaluatorHireId = workspaceContext.EvaluatorHireId;
            changed = true;
        }

        if (!string.Equals(latestSession.EvaluatorSandboxId, workspaceContext.EvaluatorSandboxId, StringComparison.OrdinalIgnoreCase))
        {
            latestSession.EvaluatorSandboxId = workspaceContext.EvaluatorSandboxId;
            changed = true;
        }

        if (changed)
        {
            latestSession.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return latestSession;
    }

    private async Task UpdateSessionStatusAsync(
        EvaluationSessionEntity sessionEntity,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        sessionEntity.Status = string.IsNullOrWhiteSpace(status) ? sessionEntity.Status : status.Trim().ToLowerInvariant();
        sessionEntity.LastError = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
        sessionEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EvaluationAssetEntity> PersistTextAssetAsync(
        EvaluationSessionEntity sessionEntity,
        string assetType,
        string relatedKey,
        string fileName,
        string content,
        string mimeType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var stored = await evaluationAssetStore.SaveTextAsync(
            sessionEntity.SessionId,
            sessionEntity.Iteration,
            assetType,
            fileName,
            content,
            mimeType,
            cancellationToken);

        var entity = new EvaluationAssetEntity
        {
            Id = Guid.NewGuid(),
            SessionEntityId = sessionEntity.Id,
            AssetType = NormalizeAssetType(assetType),
            RelatedKey = string.IsNullOrWhiteSpace(relatedKey) ? null : relatedKey.Trim(),
            RelativePath = stored.RelativePath,
            PublicUrl = stored.PublicUrl,
            MimeType = stored.MimeType,
            Size = stored.Size,
            ContentHash = stored.ContentHash,
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? "system" : sourceType.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.EvaluationAssets.Add(entity);
        sessionEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private async Task<EvaluationAssetEntity> PersistBinaryAssetAsync(
        EvaluationSessionEntity sessionEntity,
        string assetType,
        string relatedKey,
        string fileName,
        byte[] content,
        string mimeType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var stored = await evaluationAssetStore.SaveBytesAsync(
            sessionEntity.SessionId,
            sessionEntity.Iteration,
            assetType,
            fileName,
            content,
            mimeType,
            cancellationToken);

        var entity = new EvaluationAssetEntity
        {
            Id = Guid.NewGuid(),
            SessionEntityId = sessionEntity.Id,
            AssetType = NormalizeAssetType(assetType),
            RelatedKey = string.IsNullOrWhiteSpace(relatedKey) ? null : relatedKey.Trim(),
            RelativePath = stored.RelativePath,
            PublicUrl = stored.PublicUrl,
            MimeType = stored.MimeType,
            Size = stored.Size,
            ContentHash = stored.ContentHash,
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? "system" : sourceType.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.EvaluationAssets.Add(entity);
        sessionEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var fromTarget = await LoadTestcaseSourcesFromTargetArtifactsAsync(workspaceContext.TargetHireId, cancellationToken);
        if (fromTarget.Count > 0)
        {
            return fromTarget;
        }

        var templateHints = BuildTemplateHints(employee);
        return await LoadTestcaseSourcesFromFixtureAsync(
            workspaceContext.TargetHireId,
            templateHints,
            cancellationToken);
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromTargetArtifactsAsync(
        string targetHireId,
        CancellationToken cancellationToken)
    {
        var packageSnapshot = await artifactPackageService.GetLatestPackageAsync(targetHireId, cancellationToken);
        if (packageSnapshot?.Content is not { Length: > 0 })
        {
            return [];
        }

        var sources = new List<TestcaseSourceFile>();
        try
        {
            using var stream = new MemoryStream(packageSnapshot.Content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name) || !entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                var json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                var normalizedPath = entry.FullName.Replace('\\', '/');
                var isTestcaseFolderEntry = normalizedPath.StartsWith("testcases/", StringComparison.OrdinalIgnoreCase);
                if (!isTestcaseFolderEntry &&
                    !json.Contains("test_case", StringComparison.OrdinalIgnoreCase) &&
                    !json.Contains("test_cases", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sources.Add(new TestcaseSourceFile(
                    FileName: Path.GetFileName(normalizedPath),
                    SourcePath: normalizedPath,
                    RawJson: json,
                    SourceType: packageSnapshot.Kind));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract target artifact testcase files. TargetHireId={TargetHireId}", targetHireId);
        }

        return sources;
    }

    private static async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromFixtureAsync(
        string targetHireId,
        IReadOnlyList<string> templateHints,
        CancellationToken cancellationToken)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return [];
        }

        // Prefer testcase bundle colocated with the target fixture package.
        var scopedRoot = ResolveScopedFixtureTestcaseRoot(fixtureRoot, targetHireId);
        var scopedSources = await LoadTestcaseSourcesFromDirectoryAsync(scopedRoot, "fixture-scoped", cancellationToken);
        if (scopedSources.Count > 0)
        {
            return scopedSources;
        }

        var templateScopedRoots = ResolveTemplateScopedFixtureTestcaseRoots(fixtureRoot, templateHints);
        foreach (var templateScopedRoot in templateScopedRoots)
        {
            var templateScopedSources = await LoadTestcaseSourcesFromDirectoryAsync(
                templateScopedRoot,
                "fixture-template-scoped",
                cancellationToken);
            if (templateScopedSources.Count > 0)
            {
                return templateScopedSources;
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromDirectoryAsync(
        string? sourceDirectory,
        string sourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return [];
        }

        var files = Directory.GetFiles(sourceDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        var sources = new List<TestcaseSourceFile>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            sources.Add(new TestcaseSourceFile(
                FileName: Path.GetFileName(file),
                SourcePath: file,
                RawJson: content,
                SourceType: sourceType));
        }

        return sources;
    }

    private async Task<ApiResponse<TargetArtifactWarmupResult>> EnsureTargetArtifactBundleLoadedAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        bool forceRefresh,
        string? explicitArtifactPath,
        CancellationToken cancellationToken)
    {
        var warmupKey = $"{BuildWorkspaceKey(owner, employee.EmployeeId)}::{workspaceContext.TargetHireId}";
        if (!forceRefresh && TargetArtifactPrimed.ContainsKey(warmupKey))
        {
            return ApiResponse<TargetArtifactWarmupResult>.SuccessResponse(new TargetArtifactWarmupResult(
                WorkspacePath: "hiring-conversation",
                SourceArtifactPath: "already-primed"));
        }

        var bundleResult = await BuildTargetArtifactBundleAsync(
            workspaceContext.TargetHireId,
            employee,
            explicitArtifactPath,
            cancellationToken);
        if (!bundleResult.Success || bundleResult.Data is null)
        {
            return ApiResponse<TargetArtifactWarmupResult>.ErrorResponse(bundleResult.Code, bundleResult.Message);
        }

        var bundle = bundleResult.Data;
        var zipAsset = await PersistBinaryAssetAsync(
            sessionEntity,
            assetType: "target-artifact-zip",
            relatedKey: $"target-artifact:{workspaceContext.TargetHireId}",
            fileName: bundle.FileName,
            content: bundle.Content,
            mimeType: "application/zip",
            sourceType: bundle.SourceType,
            cancellationToken);

        var startConversationResult = await EnsureSandboxConversationStartedAsync(
            employee.OwnerUserId,
            workspaceContext.TargetHireId,
            workspaceContext.TargetSandboxId,
            "evaluation-target",
            cancellationToken);
        if (!startConversationResult.Success && startConversationResult.Code != 409)
        {
            logger.LogInformation(
                "Target conversation start for artifact warmup skipped. TargetHireId={TargetHireId}, Code={Code}, Message={Message}",
                workspaceContext.TargetHireId,
                startConversationResult.Code,
                startConversationResult.Message);
        }

        var zipBase64 = Convert.ToBase64String(bundle.Content);
        var warmupMessage = BuildTargetArtifactWarmupPrompt(bundle.FileName, zipAsset.PublicUrl);
        var warmupSendResult = await SendSandboxMessageAsync(
            employee.OwnerUserId,
            workspaceContext.TargetHireId,
            workspaceContext.TargetSandboxId,
            "evaluation-target",
            new HiringConversationMessageRequestDto
            {
                Content = warmupMessage,
                StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["artifact_bundle_name"] = bundle.FileName,
                    ["artifact_bundle_sha256"] = bundle.Sha256,
                    ["artifact_bundle_public_url"] = zipAsset.PublicUrl,
                    ["artifact_bundle_source"] = bundle.SourceType
                },
                Materials =
                [
                    new HiringConversationMaterialDto
                    {
                        Type = "file",
                        Name = bundle.FileName,
                        Content = zipBase64,
                        ContentHash = bundle.Sha256,
                        Size = bundle.Content.LongLength,
                        MimeType = "application/zip",
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["encoding"] = "base64",
                            ["source_type"] = bundle.SourceType,
                            ["source_path"] = bundle.SourcePath
                        }
                    }
                ]
            },
            cancellationToken);

        if (!warmupSendResult.Success)
        {
            return ApiResponse<TargetArtifactWarmupResult>.ErrorResponse(
                warmupSendResult.Code,
                $"failed to send target artifact attachment: {warmupSendResult.Message}");
        }

        TargetArtifactPrimed[warmupKey] = 0;
        await UpdateSessionStatusAsync(sessionEntity, "target_artifact_primed", null, cancellationToken);

        return ApiResponse<TargetArtifactWarmupResult>.SuccessResponse(new TargetArtifactWarmupResult(
            WorkspacePath: "hiring-conversation",
            SourceArtifactPath: bundle.SourcePath));
    }

    private async Task<ApiResponse<TargetArtifactBundle>> BuildTargetArtifactBundleAsync(
        string targetHireId,
        EmployeeDetailDto employee,
        string? explicitArtifactPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitArtifactPath))
        {
            var normalizedPath = explicitArtifactPath.Trim();
            if (Directory.Exists(normalizedPath))
            {
                var explicitZip = await ZipDirectoryAsBundleAsync(
                    normalizedPath,
                    $"{Path.GetFileName(normalizedPath)}.zip",
                    sourceType: "explicit-directory",
                    cancellationToken);
                return ApiResponse<TargetArtifactBundle>.SuccessResponse(explicitZip);
            }

            if (File.Exists(normalizedPath) &&
                normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await File.ReadAllBytesAsync(normalizedPath, cancellationToken);
                if (bytes.Length > 0)
                {
                    var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    return ApiResponse<TargetArtifactBundle>.SuccessResponse(new TargetArtifactBundle(
                        FileName: Path.GetFileName(normalizedPath),
                        Content: bytes,
                        Sha256: hash,
                        SourceType: "explicit-zip",
                        SourcePath: normalizedPath));
                }
            }

            return ApiResponse<TargetArtifactBundle>.ErrorResponse(404, $"explicit artifact path not found: {normalizedPath}");
        }

        var packageSnapshot = await artifactPackageService.GetLatestPackageAsync(targetHireId, cancellationToken);
        if (packageSnapshot?.Content is { Length: > 0 })
        {
            var sourceName = string.IsNullOrWhiteSpace(packageSnapshot.FileName)
                ? $"hiring_artifacts_{targetHireId}.zip"
                : packageSnapshot.FileName;
            var hash = Convert.ToHexStringLower(SHA256.HashData(packageSnapshot.Content));
            return ApiResponse<TargetArtifactBundle>.SuccessResponse(new TargetArtifactBundle(
                FileName: sourceName,
                Content: packageSnapshot.Content,
                Sha256: hash,
                SourceType: packageSnapshot.Kind,
                SourcePath: targetHireId));
        }

        return ApiResponse<TargetArtifactBundle>.ErrorResponse(404, "target artifact package not found");
    }

    private static async Task<TargetArtifactBundle> ZipDirectoryAsBundleAsync(
        string sourceDirectory,
        string fileName,
        string sourceType,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream, cancellationToken);
            }
        }

        var bytes = memoryStream.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new TargetArtifactBundle(
            FileName: string.IsNullOrWhiteSpace(fileName) ? "hiring-artifacts.zip" : fileName,
            Content: bytes,
            Sha256: hash,
            SourceType: sourceType,
            SourcePath: sourceDirectory);
    }

    private static string? ResolveFixtureArtifactDirectory(string targetHireId, EmployeeDetailDto employee)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return null;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(targetHireId))
        {
            var normalizedHireId = targetHireId.Trim();
            candidates.Add(Path.Combine(fixtureRoot, normalizedHireId));
            candidates.Add(Path.Combine(fixtureRoot, normalizedHireId.Replace("hire_", "e_", StringComparison.OrdinalIgnoreCase)));
        }

        var templateHints = BuildTemplateHints(employee);
        foreach (var hint in templateHints.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            candidates.Add(Path.Combine(fixtureRoot, hint.Trim()));
            var binding = ResolveFixtureTemplateBinding(hint);
            if (!string.IsNullOrWhiteSpace(binding?.FixtureEmployeeId))
            {
                var fixtureEmployeeId = binding.FixtureEmployeeId!.Trim();
                candidates.Add(Path.Combine(fixtureRoot, fixtureEmployeeId));
                if (fixtureEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(Path.Combine(fixtureRoot, $"hire_{fixtureEmployeeId[2..]}"));
                }
            }
        }

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(Path.GetFullPath)
            .FirstOrDefault(path =>
                Directory.Exists(path) &&
                (File.Exists(Path.Combine(path, "instance.json")) || Directory.Exists(Path.Combine(path, "testcases"))));
    }

    private static string BuildTargetArtifactWarmupPrompt(string fileName, string publicUrl)
    {
        return $"""
                [ArtifactWarmup]
                你将收到一个压缩包附件：{fileName}
                请先解压并完整学习其中的全部资料（config/skills/ontology/testcases 等），再执行后续测试场景。
                附件的资源链接（如需校验）：{publicUrl}
                学习完成后请回复：READY_FOR_EVALUATION
                """;
    }

    private async Task<OntologyProfile> BuildOntologyProfileAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var sources = await LoadOntologySourcesAsync(workspaceContext, employee, cancellationToken);
        var rules = BuildOntologyRulesFromSources(sources);
        var normalizedRules = rules.Count == 0
            ? DefaultOntologyRules.ToArray()
            : rules;

        var sourceSummary = sources.Count == 0
            ? "default-ontology"
            : string.Join(
                ",",
                sources
                    .Select(item => item.SourceType)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

        return new OntologyProfile(
            DimensionWeights: new Dictionary<string, decimal>(DefaultOntologyWeights, StringComparer.OrdinalIgnoreCase),
            DimensionRules: normalizedRules,
            Sources: sources,
            SourceSummary: sourceSummary);
    }

    private async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var fromTarget = await LoadOntologySourcesFromTargetArtifactsAsync(workspaceContext.TargetHireId, cancellationToken);
        if (fromTarget.Count > 0)
        {
            return fromTarget;
        }

        var templateHints = BuildTemplateHints(employee);
        return await LoadOntologySourcesFromFixtureAsync(
            workspaceContext.TargetHireId,
            templateHints,
            cancellationToken);
    }

    private async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesFromTargetArtifactsAsync(
        string targetHireId,
        CancellationToken cancellationToken)
    {
        var packageSnapshot = await artifactPackageService.GetLatestPackageAsync(targetHireId, cancellationToken);
        if (packageSnapshot?.Content is not { Length: > 0 })
        {
            return [];
        }

        var sources = new List<OntologySourceFile>();
        try
        {
            using var stream = new MemoryStream(packageSnapshot.Content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                var normalizedPath = entry.FullName.Replace('\\', '/');
                if (!normalizedPath.StartsWith("ontology/", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith("ontology/hiring-session/", StringComparison.OrdinalIgnoreCase) ||
                    !IsOntologyFileExtension(normalizedPath))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                var content = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                sources.Add(new OntologySourceFile(
                    FileName: Path.GetFileName(normalizedPath),
                    SourcePath: normalizedPath,
                    Content: content,
                    SourceType: packageSnapshot.Kind));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract ontology files from target artifacts. TargetHireId={TargetHireId}", targetHireId);
        }

        return sources;
    }

    private static async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesFromFixtureAsync(
        string targetHireId,
        IReadOnlyList<string> templateHints,
        CancellationToken cancellationToken)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return [];
        }

        var scopedRoot = ResolveScopedFixtureOntologyRoot(fixtureRoot, targetHireId);
        var scopedSources = await LoadOntologySourcesFromDirectoryAsync(scopedRoot, "fixture-scoped", cancellationToken);
        if (scopedSources.Count > 0)
        {
            return scopedSources;
        }

        var templateScopedRoots = ResolveTemplateScopedFixtureOntologyRoots(fixtureRoot, templateHints);
        foreach (var templateScopedRoot in templateScopedRoots)
        {
            var templateScopedSources = await LoadOntologySourcesFromDirectoryAsync(
                templateScopedRoot,
                "fixture-template-scoped",
                cancellationToken);
            if (templateScopedSources.Count > 0)
            {
                return templateScopedSources;
            }
        }

        var globalRoot = Path.Combine(fixtureRoot, "ontology");
        return await LoadOntologySourcesFromDirectoryAsync(globalRoot, "fixture-global", cancellationToken);
    }

    private static async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesFromDirectoryAsync(
        string? sourceDirectory,
        string sourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return [];
        }

        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsOntologyFileExtension)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        var sources = new List<OntologySourceFile>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            sources.Add(new OntologySourceFile(
                FileName: Path.GetFileName(file),
                SourcePath: file,
                Content: content,
                SourceType: sourceType));
        }

        return sources;
    }

    private static bool IsOntologyFileExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildOntologyRulesFromSources(IReadOnlyList<OntologySourceFile> sources)
    {
        if (sources.Count == 0)
        {
            return [];
        }

        var candidates = new List<OntologyRuleCandidate>();
        foreach (var source in sources)
        {
            if (LooksLikeJson(source.Content))
            {
                candidates.AddRange(ParseOntologyRuleCandidatesFromJson(source));
            }
            else
            {
                candidates.AddRange(ParseOntologyRuleCandidatesFromMarkdown(source));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var selectedRules = new List<string>();
        var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dimension in DefaultOntologyWeights.Keys)
        {
            var candidate = candidates.FirstOrDefault(item =>
                item.Dimension.Equals(dimension, StringComparison.OrdinalIgnoreCase));
            if (candidate is null || !seenTexts.Add(candidate.Text))
            {
                continue;
            }

            selectedRules.Add(
                $"{ToDimensionDisplayName(dimension)}: {candidate.Text} (source: {candidate.SourceFile})");
        }

        foreach (var candidate in candidates)
        {
            if (selectedRules.Count >= 10 || !seenTexts.Add(candidate.Text))
            {
                continue;
            }

            selectedRules.Add(
                $"{ToDimensionDisplayName(candidate.Dimension)}: {candidate.Text} (source: {candidate.SourceFile})");
        }

        foreach (var defaultRule in DefaultOntologyRules)
        {
            if (selectedRules.Count >= 10)
            {
                break;
            }

            var defaultRuleKey = defaultRule.Split(':', 2)[0].Trim();
            var alreadyCovered = selectedRules.Any(rule =>
                rule.StartsWith(defaultRuleKey + ":", StringComparison.OrdinalIgnoreCase));
            if (!alreadyCovered)
            {
                selectedRules.Add(defaultRule);
            }
        }

        return selectedRules;
    }

    private static IReadOnlyList<OntologyRuleCandidate> ParseOntologyRuleCandidatesFromMarkdown(OntologySourceFile source)
    {
        var rules = new List<OntologyRuleCandidate>();
        var section = string.Empty;
        foreach (var rawLine in source.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith('#'))
            {
                section = rawLine.TrimStart('#').Trim();
                continue;
            }

            var text = TryExtractListItem(rawLine);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 6)
            {
                continue;
            }

            rules.Add(new OntologyRuleCandidate(
                Dimension: InferOntologyDimension(section, text),
                Text: text,
                SourceFile: source.FileName));
        }

        return rules;
    }

    private static IReadOnlyList<OntologyRuleCandidate> ParseOntologyRuleCandidatesFromJson(OntologySourceFile source)
    {
        try
        {
            using var document = JsonDocument.Parse(source.Content);
            var rules = new List<OntologyRuleCandidate>();
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return rules;
            }

            if (root.TryGetProperty("rules", out var rulesElement) &&
                rulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var ruleElement in rulesElement.EnumerateArray())
                {
                    if (ruleElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var text = ruleElement.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    rules.Add(new OntologyRuleCandidate(
                        Dimension: InferOntologyDimension(string.Empty, text),
                        Text: text,
                        SourceFile: source.FileName));
                }
            }

            if (root.TryGetProperty("dimensionRules", out var dimensionRulesElement) &&
                dimensionRulesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in dimensionRulesElement.EnumerateObject())
                {
                    var dimension = NormalizeOntologyDimension(property.Name);
                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                        {
                            var text = property.Value.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                rules.Add(new OntologyRuleCandidate(dimension, text, source.FileName));
                            }

                            break;
                        }
                        case JsonValueKind.Array:
                        {
                            foreach (var ruleElement in property.Value.EnumerateArray())
                            {
                                if (ruleElement.ValueKind != JsonValueKind.String)
                                {
                                    continue;
                                }

                                var text = ruleElement.GetString()?.Trim();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    rules.Add(new OntologyRuleCandidate(dimension, text, source.FileName));
                                }
                            }

                            break;
                        }
                    }
                }
            }

            if (root.TryGetProperty("dimensions", out var dimensionsElement) &&
                dimensionsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in dimensionsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (property.Value.TryGetProperty("rule", out var ruleElement) &&
                        ruleElement.ValueKind == JsonValueKind.String)
                    {
                        var text = ruleElement.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            rules.Add(new OntologyRuleCandidate(
                                NormalizeOntologyDimension(property.Name),
                                text,
                                source.FileName));
                        }
                    }

                    if (property.Value.TryGetProperty("description", out var descriptionElement) &&
                        descriptionElement.ValueKind == JsonValueKind.String)
                    {
                        var text = descriptionElement.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            rules.Add(new OntologyRuleCandidate(
                                NormalizeOntologyDimension(property.Name),
                                text,
                                source.FileName));
                        }
                    }
                }
            }

            return rules;
        }
        catch
        {
            return [];
        }
    }

    private static string? TryExtractListItem(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
        {
            return trimmed[2..].Trim();
        }

        var separatorIndex = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (separatorIndex <= 0 || !int.TryParse(trimmed[..separatorIndex], out _))
        {
            return null;
        }

        return trimmed[(separatorIndex + 2)..].Trim();
    }

    private static bool LooksLikeJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            return ch is '{' or '[';
        }

        return false;
    }

    private static string InferOntologyDimension(string section, string text)
    {
        var normalized = $"{section} {text}".ToLowerInvariant();
        if (ContainsAny(normalized, "compliance", "constraint", "policy", "approval", "approve", "sign-off", "risk", "合规", "约束", "审批", "必须", "管控"))
        {
            return "compliance";
        }

        if (ContainsAny(normalized, "communication", "clear", "polite", "actionable", "沟通", "表达", "易读", "清晰"))
        {
            return "communication";
        }

        if (ContainsAny(normalized, "accuracy", "entity", "fact", "domain", "context", "精准", "准确", "实体"))
        {
            return "accuracy";
        }

        if (ContainsAny(normalized, "complete", "completeness", "action", "step", "workflow", "lifecycle", "流程", "步骤", "闭环", "全生命周期", "任务"))
        {
            return "completeness";
        }

        return "completeness";
    }

    private static string NormalizeOntologyDimension(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "completeness";
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.Contains("accuracy") || normalized.Contains("准确"))
        {
            return "accuracy";
        }

        if (normalized.Contains("complete") || normalized.Contains("completeness") || normalized.Contains("完整"))
        {
            return "completeness";
        }

        if (normalized.Contains("compliance") || normalized.Contains("合规"))
        {
            return "compliance";
        }

        if (normalized.Contains("communication") || normalized.Contains("沟通"))
        {
            return "communication";
        }

        return normalized;
    }

    private static string ToDimensionDisplayName(string dimension)
    {
        var normalized = NormalizeOntologyDimension(dimension);
        return normalized switch
        {
            "accuracy" => "Accuracy",
            "completeness" => "Completeness",
            "compliance" => "Compliance",
            "communication" => "Communication",
            _ => normalized
        };
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) &&
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<ParsedTestcase> ParseTestcases(
        string sourceFile,
        string sourcePath,
        string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            var caseElements = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("test_cases", out var testCasesElement) &&
                testCasesElement.ValueKind == JsonValueKind.Array)
            {
                caseElements.AddRange(testCasesElement.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                caseElements.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("test_case_id", out _))
            {
                caseElements.Add(root);
            }
            else
            {
                return [];
            }

            var parsed = new List<ParsedTestcase>();
            for (var index = 0; index < caseElements.Count; index++)
            {
                var caseElement = caseElements[index];
                var testcaseId = TryGetString(caseElement, "test_case_id", $"{Path.GetFileNameWithoutExtension(sourceFile)}-{index + 1:D2}");
                var scenarioName = TryGetString(caseElement, "scenario_name", testcaseId);
                var expectedSteps = ParseExpectedSteps(caseElement);
                var rawCase = caseElement.GetRawText();
                var inputPrompt = TryReadUserRequestFromRawTestcase(rawCase) ?? scenarioName;

                parsed.Add(new ParsedTestcase(
                    TestcaseId: testcaseId,
                    ScenarioName: scenarioName,
                    SourceFile: sourceFile,
                    SourcePath: sourcePath,
                    RawJson: rawCase,
                    ExpectedSteps: expectedSteps,
                    InputPrompt: inputPrompt));
            }

            return parsed;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseExpectedSteps(JsonElement testcaseElement)
    {
        if (!testcaseElement.TryGetProperty("expected_behavior_sequence", out var stepsElement) ||
            stepsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var steps = new List<string>();
        foreach (var step in stepsElement.EnumerateArray())
        {
            var action = TryGetString(step, "action", string.Empty);
            var criteria = TryGetString(step, "criteria", string.Empty);
            var order = TryGetString(step, "step", string.Empty);
            if (string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(criteria))
            {
                continue;
            }

            var rendered = string.IsNullOrWhiteSpace(order)
                ? $"{action} | {criteria}".Trim(' ', '|')
                : $"{order}. {action} | {criteria}".Trim(' ', '|');
            steps.Add(rendered);
        }

        return steps;
    }

    private static IReadOnlyList<EvaluationQuestionCardDto> BuildQuestionCards(IReadOnlyList<ParsedTestcase> parsedTestcases)
    {
        return parsedTestcases
            .GroupBy(
                testcase => string.IsNullOrWhiteSpace(testcase.TestcaseId)
                    ? testcase.ScenarioName
                    : testcase.TestcaseId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
            .Select(testcase => new EvaluationQuestionCardDto(
                TestcaseId: testcase.TestcaseId,
                Title: testcase.ScenarioName,
                Prompt: testcase.InputPrompt,
                ScoringHint: "Score by ontology dimensions and expected behavior alignment.",
                Steps: testcase.ExpectedSteps,
                SourceFile: testcase.SourceFile))
            .ToArray();
    }

    private async Task<IReadOnlyList<EvaluationQuestionCardDto>> BuildQuestionCardsFromAssetsAsync(
        IReadOnlyList<EvaluationAssetEntity> testcaseAssets,
        CancellationToken cancellationToken)
    {
        var parsed = new List<ParsedTestcase>();
        foreach (var testcaseAsset in testcaseAssets)
        {
            var physicalPath = ResolvePhysicalAssetPath(testcaseAsset.RelativePath);
            if (string.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath))
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(physicalPath, cancellationToken);
            parsed.AddRange(ParseTestcases(Path.GetFileName(physicalPath), testcaseAsset.RelativePath, json));
        }

        return BuildQuestionCards(parsed);
    }

    private async Task<IReadOnlyList<EvaluationQuestionCardDto>?> LoadQuestionCardsForSessionAsync(
        Guid sessionEntityId,
        CancellationToken cancellationToken)
    {
        var allAssets = await dbContext.EvaluationAssets
            .AsNoTracking()
            .Where(item =>
                item.SessionEntityId == sessionEntityId &&
                item.AssetType == "testcases-json")
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (allAssets.Count == 0)
        {
            return null;
        }

        var deduplicated = allAssets
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.RelatedKey)
                    ? item.RelativePath
                    : item.RelatedKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(5)
            .ToArray();

        var cards = await BuildQuestionCardsFromAssetsAsync(deduplicated, cancellationToken);
        return cards.Count > 0 ? cards : null;
    }

    private async Task<IReadOnlyList<EvaluationQuestionCardDto>?> LoadQuestionCardsForLatestSessionAsync(
        string owner,
        string employeeId,
        CancellationToken cancellationToken)
    {
        var latestSession = await dbContext.EvaluationSessions
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == owner &&
                item.EmployeeId == employeeId.Trim())
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSession is null)
        {
            return null;
        }

        return await LoadQuestionCardsForSessionAsync(latestSession.Id, cancellationToken);
    }

    private static bool HasMaterialsSupplementPrompt(IReadOnlyList<HiringConversationMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            return false;
        }

        return messages.Any(message =>
            string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(message.Content) &&
            message.Content.Contains("评估资料不完整", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasEvaluationReadyPrompt(IReadOnlyList<HiringConversationMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            return false;
        }

        return messages.Any(message =>
            string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(message.Content) &&
            (message.Content.Contains("以下是本轮考题卡片", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("评分标准（按评估本体维度）", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("你可以继续对话询问题卡细节、评分标准", StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildQuestionCardsMarkdown(IReadOnlyList<EvaluationQuestionCardDto> cards)
    {
        if (cards.Count == 0)
        {
            return "当前未解析到可展示的考题卡片，请先确认测试用例是否已成功加载。";
        }

        var lines = new List<string>
        {
            "以下是本轮考题卡片："
        };

        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            lines.Add($"{index + 1}. [{card.TestcaseId}] {card.Title}");
            lines.Add($"   题目：{card.Prompt}");
            if (card.Steps.Count > 0)
            {
                lines.Add($"   关键步骤：{string.Join("；", card.Steps)}");
            }

            if (!string.IsNullOrWhiteSpace(card.ScoringHint))
            {
                lines.Add($"   判分提示：{card.ScoringHint}");
            }
        }

        lines.Add("如需我解释某一题的评分标准，请直接说“解释第 N 题”。");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOntologyRulesMarkdown()
    {
        var lines = new List<string>
        {
            "评分标准（按评估本体维度）："
        };

        foreach (var weight in DefaultOntologyWeights)
        {
            lines.Add($"- {ToDimensionDisplayName(weight.Key)}（权重 {weight.Value:0.##}）");
        }

        foreach (var rule in DefaultOntologyRules)
        {
            lines.Add($"- {rule}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildMissingMaterialsSummary(bool testcaseReady, bool ontologyReady)
    {
        if (!testcaseReady && !ontologyReady)
        {
            return "缺失测试用例与评估本体";
        }

        if (!testcaseReady)
        {
            return "缺失测试用例";
        }

        if (!ontologyReady)
        {
            return "缺失评估本体";
        }

        return "测试用例与评估本体均已就绪";
    }

    private static string NormalizeEvaluationStatus(string? sessionStatus)
    {
        if (string.IsNullOrWhiteSpace(sessionStatus))
        {
            return "pending";
        }

        return sessionStatus.Trim().ToLowerInvariant();
    }

    private static string BuildEvaluationRecommendation(
        EvaluationReportSummaryDto? latestReport,
        EvaluationReadinessDto? readiness,
        string sessionStatus)
    {
        if (latestReport is not null)
        {
            return latestReport.Passed
                ? "Evaluation report passed. Submit human review decision to continue onboarding."
                : "Evaluation report failed. Fix issues from report and rerun evaluation.";
        }

        if (readiness is null)
        {
            return "Evaluation session exists but readiness data is unavailable yet.";
        }

        if (!readiness.TestcasesReady && !readiness.OntologyReady)
        {
            return "Testcases and ontology are not ready. Place testcase JSON files under 'testcases/' and ontology files under 'ontology/' in the target sandbox artifact, then rerun LOAD_SKILL or START.";
        }

        if (!readiness.TestcasesReady)
        {
            return "Testcases are not ready. Place testcase JSON files (with 'test_case' fields) under 'testcases/' in the target sandbox artifact, then rerun LOAD_SKILL or START.";
        }

        if (!readiness.OntologyReady)
        {
            return "Ontology is not ready. Place ontology .md/.txt/.json files (with dimension and rule definitions) under 'ontology/' in the target sandbox artifact, then rerun LOAD_SKILL or START.";
        }

        return sessionStatus switch
        {
            "ready" => "Testcases and ontology are ready. Confirm question cards and run evaluation.",
            "target_executed" => "Target execution trace captured. Run scoring and persist report.",
            _ => "Evaluation session is active. Continue the next evaluation step."
        };
    }

    private static IReadOnlyList<EvaluationScenarioDto> BuildScenariosFromQuestionCards(
        IReadOnlyList<EvaluationQuestionCardDto> questionCards,
        EvaluationSessionEntity sessionEntity,
        EvaluationReportSummaryDto? latestReport)
    {
        if (questionCards.Count == 0)
        {
            return [];
        }

        var scenarioStatus = latestReport is null
            ? "pending"
            : "completed";
        var verdict = latestReport is null
            ? null
            : latestReport.Passed
                ? "passed"
                : "failed";
        var verdictComment = latestReport is null
            ? null
            : "Verdict derived from latest evaluation report.";
        var startedAtUtc = sessionEntity.CreatedAtUtc.ToString("o");
        var completedAtUtc = latestReport?.CreatedAtUtc;

        return questionCards
            .Select(card => new EvaluationScenarioDto(
                ScenarioId: card.TestcaseId,
                ScenarioName: card.Title,
                Status: scenarioStatus,
                Verdict: verdict,
                VerdictComment: verdictComment,
                MessageCount: 0,
                StartedAt: startedAtUtc,
                CompletedAt: completedAtUtc))
            .ToArray();
    }

    private static EvaluationScenarioDto BuildSummaryScenario(
        EvaluationSessionEntity sessionEntity,
        EvaluationReportSummaryDto latestReport)
    {
        return new EvaluationScenarioDto(
            ScenarioId: $"report_{latestReport.Iteration}",
            ScenarioName: $"评估轮次 #{latestReport.Iteration}",
            Status: "completed",
            Verdict: latestReport.Passed ? "passed" : "failed",
            VerdictComment: "该结果由最新落库评估报告生成。",
            MessageCount: 0,
            StartedAt: sessionEntity.CreatedAtUtc.ToString("o"),
            CompletedAt: latestReport.CreatedAtUtc);
    }

    private static string BuildTargetExecutionPrompt(string testcaseId, string input)
    {
        return $"""
                [EvaluationExecution]
                testcase_id: {testcaseId}
                execute the following scenario input as target employee:
                {input}
                return actionable response for evaluation trace capture.
                """;
    }

    private static string BuildReportHtml(object payload, IReadOnlyList<EvaluationDimensionScoreDto> dimensionScores)
    {
        static string LocalizeDimensionName(string dimension)
        {
            return dimension.Trim().ToLowerInvariant() switch
            {
                "accuracy" => "准确性",
                "completeness" => "完整性",
                "compliance" => "合规性",
                "communication" => "沟通质量",
                _ => string.IsNullOrWhiteSpace(dimension) ? "未命名维度" : dimension.Trim()
            };
        }

        static string ScoreLevel(decimal score)
        {
            return score switch
            {
                >= 85m => "优秀",
                >= 70m => "良好",
                >= 60m => "合格",
                _ => "待改进"
            };
        }

        static string ScoreColor(decimal score)
        {
            return score switch
            {
                >= 85m => "#10b981",
                >= 70m => "#3b82f6",
                >= 60m => "#f59e0b",
                _ => "#ef4444"
            };
        }

        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonOptions);
        string? summary = null;
        string? generatedAtUtc = null;
        string? employeeId = null;
        string? sessionId = null;
        int? iteration = null;
        decimal? overallScore = null;
        bool? passed = null;

        if (payloadElement.ValueKind == JsonValueKind.Object)
        {
            if (payloadElement.TryGetProperty("summary", out var summaryProperty) &&
                summaryProperty.ValueKind == JsonValueKind.String)
            {
                summary = summaryProperty.GetString();
            }

            if (payloadElement.TryGetProperty("generatedAtUtc", out var generatedAtProperty) &&
                generatedAtProperty.ValueKind == JsonValueKind.String)
            {
                generatedAtUtc = generatedAtProperty.GetString();
            }

            if (payloadElement.TryGetProperty("employeeId", out var employeeProperty) &&
                employeeProperty.ValueKind == JsonValueKind.String)
            {
                employeeId = employeeProperty.GetString();
            }

            if (payloadElement.TryGetProperty("sessionId", out var sessionProperty) &&
                sessionProperty.ValueKind == JsonValueKind.String)
            {
                sessionId = sessionProperty.GetString();
            }

            if (payloadElement.TryGetProperty("iteration", out var iterationProperty) &&
                iterationProperty.ValueKind is JsonValueKind.Number &&
                iterationProperty.TryGetInt32(out var parsedIteration))
            {
                iteration = parsedIteration;
            }

            if (payloadElement.TryGetProperty("overallScore", out var scoreProperty) &&
                scoreProperty.ValueKind is JsonValueKind.Number &&
                scoreProperty.TryGetDecimal(out var parsedScore))
            {
                overallScore = parsedScore;
            }

            if (payloadElement.TryGetProperty("passed", out var passedProperty) &&
                passedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                passed = passedProperty.GetBoolean();
            }
        }

        var localizedSummary = string.IsNullOrWhiteSpace(summary)
            ? passed == true
                ? "本轮评估达到通过标准，可进入人工审核流程。"
                : passed == false
                    ? "本轮评估未达到通过标准，建议根据维度得分优化后重试。"
                    : "评估已完成，等待后续决策。"
            : summary.Trim();
        if (localizedSummary.StartsWith("Auto-evaluation passed", StringComparison.OrdinalIgnoreCase))
        {
            localizedSummary = $"自动评估完成，综合评分 {overallScore?.ToString("0.##") ?? "—"}，判定通过。";
        }
        else if (localizedSummary.StartsWith("Auto-evaluation failed", StringComparison.OrdinalIgnoreCase))
        {
            localizedSummary = $"自动评估完成，综合评分 {overallScore?.ToString("0.##") ?? "—"}，判定未通过。";
        }

        var generatedAtDisplay = DateTimeOffset.TryParse(generatedAtUtc, out var parsedGeneratedAt)
            ? parsedGeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "—";
        var scoreDisplay = overallScore?.ToString("0.##") ?? "—";
        var iterationDisplay = iteration.HasValue ? $"第 {iteration.Value} 轮" : "—";
        var statusClass = passed == true ? "status-pass" : passed == false ? "status-fail" : "status-pending";
        var statusText = passed == true ? "评估通过" : passed == false ? "评估未通过" : "评估进行中";

        var rows = dimensionScores.Count == 0
            ? "<tr><td colspan=\"5\" class=\"empty\">暂无维度评分数据</td></tr>"
            : string.Join(
                Environment.NewLine,
                dimensionScores.Select(item =>
                {
                    var score = Math.Round(Math.Clamp(item.Score, 0m, 100m), 2);
                    var width = score.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    var level = ScoreLevel(score);
                    var color = ScoreColor(score);
                    return $"""
                            <tr>
                                <td>{System.Net.WebUtility.HtmlEncode(LocalizeDimensionName(item.Dimension))}</td>
                                <td class="score">{score:0.##}</td>
                                <td>
                                    <div class="bar-track">
                                        <span class="bar-fill" style="width: {width}%; background: {color};"></span>
                                    </div>
                                </td>
                                <td>{System.Net.WebUtility.HtmlEncode(level)}</td>
                                <td>{System.Net.WebUtility.HtmlEncode(item.Comment)}</td>
                            </tr>
                            """;
                }));
        var payloadJson = System.Net.WebUtility.HtmlEncode(JsonSerializer.Serialize(payload, JsonOptions));

        return $$"""
                 <!doctype html>
                 <html lang="zh-CN">
                 <head>
                     <meta charset="utf-8" />
                     <meta name="viewport" content="width=device-width, initial-scale=1" />
                     <title>AI 评估报告</title>
                     <style>
                         :root {
                             --text-main: #0f172a;
                             --text-muted: #6b7280;
                             --line: #e5e7eb;
                             --surface: #ffffff;
                             --surface-soft: #f8fafc;
                             --accent: #4f46e5;
                         }
                         * { box-sizing: border-box; }
                         body {
                             margin: 0;
                             padding: 32px 20px;
                             background: linear-gradient(180deg, #fdf2f8 0%, #eef2ff 100%);
                             color: var(--text-main);
                             font-family: "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif;
                         }
                         .report {
                             max-width: 1080px;
                             margin: 0 auto;
                             background: var(--surface);
                             border: 1px solid var(--line);
                             border-radius: 20px;
                             overflow: hidden;
                             box-shadow: 0 18px 36px rgba(15, 23, 42, 0.08);
                         }
                         .header {
                             padding: 22px 24px 18px;
                             background: linear-gradient(120deg, #eef2ff 0%, #f8fafc 100%);
                             border-bottom: 1px solid var(--line);
                         }
                         h1 {
                             margin: 0;
                             font-size: 24px;
                             letter-spacing: 0.2px;
                         }
                         .subline {
                             margin-top: 6px;
                             color: var(--text-muted);
                             font-size: 13px;
                         }
                         .meta-grid {
                             margin-top: 16px;
                             display: grid;
                             gap: 10px;
                             grid-template-columns: repeat(4, minmax(0, 1fr));
                         }
                         .meta-card {
                             border: 1px solid var(--line);
                             border-radius: 14px;
                             background: var(--surface);
                             padding: 10px 12px;
                         }
                         .meta-label {
                             color: var(--text-muted);
                             font-size: 12px;
                         }
                         .meta-value {
                             margin-top: 4px;
                             font-size: 16px;
                             font-weight: 700;
                         }
                         .status-chip {
                             display: inline-flex;
                             align-items: center;
                             gap: 6px;
                             border-radius: 999px;
                             padding: 4px 10px;
                             font-size: 12px;
                             font-weight: 600;
                         }
                         .status-pass { color: #047857; background: #ecfdf5; border: 1px solid #a7f3d0; }
                         .status-fail { color: #b91c1c; background: #fef2f2; border: 1px solid #fecaca; }
                         .status-pending { color: #92400e; background: #fffbeb; border: 1px solid #fde68a; }
                         .section {
                             padding: 18px 24px;
                             border-bottom: 1px solid var(--line);
                         }
                         .section:last-child { border-bottom: none; }
                         h2 {
                             margin: 0 0 12px 0;
                             font-size: 17px;
                         }
                         .summary {
                             margin: 0;
                             border: 1px solid #dbeafe;
                             background: #eff6ff;
                             color: #1e3a8a;
                             border-radius: 12px;
                             padding: 10px 12px;
                             font-size: 14px;
                             line-height: 1.6;
                         }
                         table {
                             width: 100%;
                             border-collapse: collapse;
                             border: 1px solid var(--line);
                             border-radius: 14px;
                             overflow: hidden;
                             background: var(--surface);
                         }
                         th, td {
                             border-bottom: 1px solid var(--line);
                             padding: 10px 12px;
                             text-align: left;
                             vertical-align: middle;
                             font-size: 13px;
                         }
                         th {
                             background: var(--surface-soft);
                             color: var(--text-muted);
                             font-weight: 600;
                             font-size: 12px;
                         }
                         td.score {
                             font-weight: 700;
                             font-variant-numeric: tabular-nums;
                         }
                         .bar-track {
                             width: 120px;
                             height: 8px;
                             background: #e5e7eb;
                             border-radius: 999px;
                             overflow: hidden;
                         }
                         .bar-fill {
                             display: block;
                             height: 100%;
                             border-radius: 999px;
                         }
                         .empty {
                             text-align: center;
                             color: var(--text-muted);
                             padding: 16px;
                         }
                         pre {
                             margin: 0;
                             white-space: pre-wrap;
                             word-break: break-word;
                             border: 1px solid var(--line);
                             border-radius: 12px;
                             background: #f9fafb;
                             color: #1f2937;
                             padding: 12px 14px;
                             font-size: 12px;
                             line-height: 1.55;
                         }
                         @media (max-width: 900px) {
                             .meta-grid {
                                 grid-template-columns: repeat(2, minmax(0, 1fr));
                             }
                         }
                         @media (max-width: 560px) {
                             body { padding: 14px 10px; }
                             .header, .section { padding: 14px; }
                             .meta-grid { grid-template-columns: 1fr; }
                             .bar-track { width: 88px; }
                         }
                     </style>
                 </head>
                 <body>
                     <article class="report">
                         <header class="header">
                             <h1>AI 评估报告</h1>
                             <div class="subline">生成时间：{{System.Net.WebUtility.HtmlEncode(generatedAtDisplay)}}</div>
                             <div class="meta-grid">
                                 <div class="meta-card">
                                     <div class="meta-label">轮次</div>
                                     <div class="meta-value">{{System.Net.WebUtility.HtmlEncode(iterationDisplay)}}</div>
                                 </div>
                                 <div class="meta-card">
                                     <div class="meta-label">综合评分</div>
                                     <div class="meta-value">{{System.Net.WebUtility.HtmlEncode(scoreDisplay)}}</div>
                                 </div>
                                 <div class="meta-card">
                                     <div class="meta-label">员工 ID</div>
                                     <div class="meta-value" style="font-size: 12px; font-weight: 600;">{{System.Net.WebUtility.HtmlEncode(employeeId ?? "—")}}</div>
                                 </div>
                                 <div class="meta-card">
                                     <div class="meta-label">会话 ID</div>
                                     <div class="meta-value" style="font-size: 12px; font-weight: 600;">{{System.Net.WebUtility.HtmlEncode(sessionId ?? "—")}}</div>
                                 </div>
                             </div>
                             <div style="margin-top: 12px;">
                                 <span class="status-chip {{statusClass}}">{{System.Net.WebUtility.HtmlEncode(statusText)}}</span>
                             </div>
                         </header>

                         <section class="section">
                             <h2>评估摘要</h2>
                             <p class="summary">{{System.Net.WebUtility.HtmlEncode(localizedSummary)}}</p>
                         </section>

                         <section class="section">
                             <h2>维度评分</h2>
                             <table>
                                 <thead>
                                     <tr>
                                         <th>维度</th>
                                         <th>分数</th>
                                         <th>进度条</th>
                                         <th>等级</th>
                                         <th>说明</th>
                                     </tr>
                                 </thead>
                                 <tbody>
                                 {{rows}}
                                 </tbody>
                             </table>
                         </section>

                         <section class="section">
                             <h2>原始数据（JSON）</h2>
                             <pre>{{payloadJson}}</pre>
                         </section>
                     </article>
                 </body>
                 </html>
                 """;
    }

    private string? ResolvePhysicalAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalizedRelative = relativePath
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');
        if (normalizedRelative.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedRelative = normalizedRelative["resources/".Length..];
        }

        var candidate = Path.GetFullPath(Path.Combine(
            evaluationResourceRoot,
            normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(evaluationResourceRoot));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidate;
    }

    private static EvaluationReadinessDto BuildReadiness(bool testcaseReady, bool ontologyReady)
    {
        if (testcaseReady && ontologyReady)
        {
            return new EvaluationReadinessDto(
                TestcasesReady: true,
                OntologyReady: true,
                Status: "ready",
                Message: "Testcases and ontology are ready");
        }

        string message;
        string? recommendedAction;

        if (!testcaseReady && !ontologyReady)
        {
            message = "No test cases or ontology files found. Place testcase JSON files under 'testcases/' and ontology files under 'ontology/' in the target hire artifact package.";
            recommendedAction = "Upload testcase JSON files (containing 'test_case' fields and expected steps) under 'testcases/' directory, and ontology .md/.txt/.json files (defining scoring dimensions and rules) under 'ontology/' directory in the target sandbox artifact package, then rerun LOAD_SKILL or START.";
        }
        else if (!testcaseReady)
        {
            message = "No test cases found. Place testcase JSON files under 'testcases/' in the target hire artifact package.";
            recommendedAction = "Upload testcase JSON files (with 'test_case' identifiers and step definitions) under 'testcases/' in the target sandbox artifact package, then rerun LOAD_SKILL or START.";
        }
        else
        {
            message = "No ontology found. Place ontology files under 'ontology/' in the target hire artifact package.";
            recommendedAction = "Upload ontology .md, .txt, or .json files (defining evaluation dimensions, weights, and scoring rules) under 'ontology/' in the target sandbox artifact package, then rerun LOAD_SKILL or START.";
        }

        return new EvaluationReadinessDto(
            TestcasesReady: testcaseReady,
            OntologyReady: ontologyReady,
            Status: "waiting_materials",
            Message: message,
            RecommendedAction: recommendedAction);
    }

    private static string NormalizeAssetType(string assetType)
    {
        return string.IsNullOrWhiteSpace(assetType)
            ? "asset"
            : assetType.Trim().ToLowerInvariant();
    }

    private static EvaluationAssetRefDto ToAssetRef(EvaluationAssetEntity assetEntity)
    {
        return new EvaluationAssetRefDto(
            AssetType: assetEntity.AssetType,
            RelatedKey: assetEntity.RelatedKey ?? string.Empty,
            RelativePath: assetEntity.RelativePath,
            PublicUrl: assetEntity.PublicUrl,
            CreatedAtUtc: assetEntity.CreatedAtUtc.ToString("o"));
    }

    private static string BuildEvaluationSessionId()
    {
        return $"eval_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
    }

    private static string ResolveEvaluationResourceRoot(string contentRootPath, string? configuredResourceRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredResourceRoot))
        {
            return Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot", "resources"));
        }

        return Path.IsPathRooted(configuredResourceRoot)
            ? Path.GetFullPath(configuredResourceRoot.Trim())
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredResourceRoot.Trim()));
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string? ResolveFixtureRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "InstanceFixtures"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "HireBot.ApiService", "Assets", "InstanceFixtures"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "InstanceFixtures")),
            Path.Combine(AppContext.BaseDirectory, "Assets", "InstanceFixtures")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? ResolveScopedFixtureTestcaseRoot(string fixtureRoot, string targetHireId)
    {
        if (string.IsNullOrWhiteSpace(fixtureRoot) || string.IsNullOrWhiteSpace(targetHireId))
        {
            return null;
        }

        var normalizedHireId = targetHireId.Trim();
        var candidates = new[]
        {
            Path.Combine(fixtureRoot, normalizedHireId, "testcases"),
            Path.Combine(fixtureRoot, normalizedHireId.Replace("hire_", "e_", StringComparison.OrdinalIgnoreCase), "testcases")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? ResolveScopedFixtureOntologyRoot(string fixtureRoot, string targetHireId)
    {
        if (string.IsNullOrWhiteSpace(fixtureRoot) || string.IsNullOrWhiteSpace(targetHireId))
        {
            return null;
        }

        var normalizedHireId = targetHireId.Trim();
        var candidates = new[]
        {
            Path.Combine(fixtureRoot, normalizedHireId, "ontology"),
            Path.Combine(fixtureRoot, normalizedHireId.Replace("hire_", "e_", StringComparison.OrdinalIgnoreCase), "ontology")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static IReadOnlyList<string> BuildTemplateHints(EmployeeDetailDto employee)
    {
        var hints = new List<string>();
        void AddHint(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var normalized = raw.Trim();
            if (hints.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            hints.Add(normalized);
        }

        AddHint(employee.SourceTemplateId);
        AddHint(employee.BasedOnTemplateId);
        AddHint(employee.RoleName);

        var binding = ResolveFixtureTemplateBinding(employee.SourceTemplateId ?? employee.BasedOnTemplateId);
        AddHint(binding?.FixtureTemplateId);
        if (!string.IsNullOrWhiteSpace(binding?.FixtureEmployeeId))
        {
            var fixtureEmployeeId = binding.FixtureEmployeeId!.Trim();
            AddHint(fixtureEmployeeId);
            if (fixtureEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
            {
                AddHint($"hire_{fixtureEmployeeId[2..]}");
            }
        }

        return hints;
    }

    private static IReadOnlyList<string> ResolveTemplateScopedFixtureTestcaseRoots(
        string fixtureRoot,
        IReadOnlyList<string> templateHints)
    {
        if (string.IsNullOrWhiteSpace(fixtureRoot) ||
            !Directory.Exists(fixtureRoot) ||
            templateHints.Count == 0)
        {
            return [];
        }

        var resolvedRoots = new List<string>();
        var normalizedHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in templateHints.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            normalizedHints.Add(hint.Trim());
            var binding = ResolveFixtureTemplateBinding(hint);
            if (!string.IsNullOrWhiteSpace(binding?.FixtureTemplateId))
            {
                normalizedHints.Add(binding.FixtureTemplateId!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(binding?.FixtureEmployeeId))
            {
                var fixtureEmployeeId = binding.FixtureEmployeeId!.Trim();
                var byEmployee = Path.Combine(fixtureRoot, fixtureEmployeeId, "testcases");
                if (Directory.Exists(byEmployee))
                {
                    resolvedRoots.Add(byEmployee);
                }

                if (fixtureEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
                {
                    var byHire = Path.Combine(fixtureRoot, $"hire_{fixtureEmployeeId[2..]}", "testcases");
                    if (Directory.Exists(byHire))
                    {
                        resolvedRoots.Add(byHire);
                    }
                }
            }
        }

        foreach (var fixtureDirectory in Directory.GetDirectories(fixtureRoot))
        {
            var instancePath = Path.Combine(fixtureDirectory, "instance.json");
            if (!File.Exists(instancePath))
            {
                continue;
            }

            string? instanceTemplateId = null;
            string? instanceEmployeeId = null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(instancePath));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    instanceTemplateId = TryGetString(document.RootElement, "templateId");
                    instanceEmployeeId = TryGetString(document.RootElement, "employeeId");
                }
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(instanceTemplateId) &&
                string.IsNullOrWhiteSpace(instanceEmployeeId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(instanceTemplateId) &&
                !normalizedHints.Contains(instanceTemplateId.Trim()))
            {
                var normalizedEmployeeId = instanceEmployeeId?.Trim();
                var matchedByEmployeeId = !string.IsNullOrWhiteSpace(normalizedEmployeeId) &&
                                          normalizedHints.Contains(normalizedEmployeeId);
                var matchedByHireId = !string.IsNullOrWhiteSpace(normalizedEmployeeId) &&
                                      normalizedHints.Contains(
                                          normalizedEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase)
                                              ? $"hire_{normalizedEmployeeId[2..]}"
                                              : normalizedEmployeeId);
                if (!matchedByEmployeeId && !matchedByHireId)
                {
                    continue;
                }
            }

            var testcaseRoot = Path.Combine(fixtureDirectory, "testcases");
            if (Directory.Exists(testcaseRoot))
            {
                resolvedRoots.Add(testcaseRoot);
            }
        }

        return resolvedRoots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveTemplateScopedFixtureOntologyRoots(
        string fixtureRoot,
        IReadOnlyList<string> templateHints)
    {
        var testcaseRoots = ResolveTemplateScopedFixtureTestcaseRoots(fixtureRoot, templateHints);
        if (testcaseRoots.Count == 0)
        {
            return [];
        }

        var ontologyRoots = new List<string>();
        foreach (var testcaseRoot in testcaseRoots)
        {
            var fixtureDirectory = Directory.GetParent(testcaseRoot)?.FullName;
            if (string.IsNullOrWhiteSpace(fixtureDirectory))
            {
                continue;
            }

            var ontologyRoot = Path.Combine(fixtureDirectory, "ontology");
            if (Directory.Exists(ontologyRoot))
            {
                ontologyRoots.Add(ontologyRoot);
            }
        }

        return ontologyRoots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, FixtureTemplateBinding> LoadFixtureTemplateBindings()
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        var bindingPath = Path.Combine(fixtureRoot, "template-bindings.json");
        if (!File.Exists(bindingPath))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(bindingPath));
            var root = doc.RootElement;
            var items = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("bindings", out var bindings) &&
                     bindings.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(bindings.EnumerateArray());
            }

            var map = new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var templateId = TryGetString(item, "templateId");
                if (string.IsNullOrWhiteSpace(templateId))
                {
                    continue;
                }

                var fixtureTemplateId = TryGetString(item, "fixtureTemplateId");
                var fixtureEmployeeId = TryGetString(item, "fixtureEmployeeId");
                map[templateId.Trim()] = new FixtureTemplateBinding(
                    TemplateId: templateId.Trim(),
                    FixtureTemplateId: string.IsNullOrWhiteSpace(fixtureTemplateId) ? null : fixtureTemplateId.Trim(),
                    FixtureEmployeeId: string.IsNullOrWhiteSpace(fixtureEmployeeId) ? null : fixtureEmployeeId.Trim());
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static FixtureTemplateBinding? ResolveFixtureTemplateBinding(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return FixtureTemplateBindings.Value.TryGetValue(templateId.Trim(), out var binding)
            ? binding
            : null;
    }

    private static string TryGetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? fallback,
            JsonValueKind.Number => property.GetRawText(),
            _ => fallback
        };
    }

    private static string? TryReadUserRequestFromRawTestcase(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("input", out var inputElement) &&
                inputElement.ValueKind == JsonValueKind.Object)
            {
                if (inputElement.TryGetProperty("user_request", out var userRequestElement) &&
                    userRequestElement.ValueKind == JsonValueKind.String)
                {
                    return userRequestElement.GetString()?.Trim();
                }

                if (inputElement.TryGetProperty("prompt", out var promptElement) &&
                    promptElement.ValueKind == JsonValueKind.String)
                {
                    return promptElement.GetString()?.Trim();
                }
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static EvaluationSandboxConversationStateDto BuildSandboxConversationState(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        HiringConversationTimelineDto timeline,
        IReadOnlyList<EvaluationQuestionCardDto>? questionCards = null)
    {
        return new EvaluationSandboxConversationStateDto(
            EmployeeId: employee.EmployeeId,
            EvalPhase: string.IsNullOrWhiteSpace(employee.EvalPhase) ? "pending_skill_upload" : employee.EvalPhase,
            TargetHireId: workspaceContext.TargetHireId,
            TargetRuntimeId: workspaceContext.TargetHireId,
            TargetSandboxId: workspaceContext.TargetSandboxId,
            EvaluatorHireId: workspaceContext.EvaluatorHireId,
            EvaluatorRuntimeId: workspaceContext.EvaluatorHireId,
            EvaluatorSandboxId: workspaceContext.EvaluatorSandboxId,
            SessionId: timeline.SessionId,
            SkillLoadedAtUtc: workspaceContext.SkillLoadedAtUtc,
            Messages: timeline.Messages,
            QuestionCards: questionCards);
    }

    private static IReadOnlyList<EmployeeCapabilityDto> MergeEvaluationCapabilities(
        IReadOnlyList<EmployeeCapabilityDto> existingCapabilities,
        IReadOnlyList<string> evaluationSkills)
    {
        var merged = existingCapabilities.ToList();
        foreach (var skill in evaluationSkills)
        {
            var index = merged.FindIndex(item => item.Name.Equals(skill, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                merged[index] = merged[index] with { Ready = true };
                continue;
            }

            merged.Add(new EmployeeCapabilityDto(skill, true));
        }

        return merged;
    }

    private static EmployeeDetailDto BuildAiPassResult(EmployeeDetailDto employee, string? sandboxSummary = null)
    {
        return employee with
        {
            Status = "interning_human",
            LifecycleStatus = "pending human review",
            EvalPhase = "pending_human_review",
            StageSummary = string.IsNullOrWhiteSpace(sandboxSummary)
                ? "AI evaluation passed, waiting for human review"
                : $"AI evaluation passed: {sandboxSummary}",
            PrimarySignal = "Pending action: submit human review verdict",
            SignalLevel = "warn",
            PendingActions = ["Submit human review verdict"]
        };
    }

    private static EmployeeDetailDto BuildAiFailResult(EmployeeDetailDto employee, string? sandboxSummary = null)
    {
        return employee with
        {
            Status = "failed",
            LifecycleStatus = "evaluation failed",
            EvalPhase = "pending_review",
            StageSummary = string.IsNullOrWhiteSpace(sandboxSummary)
                ? "AI evaluation failed, go to Review for rollback or continue hire"
                : $"AI evaluation failed: {sandboxSummary}",
            PrimarySignal = "Pending action: choose a Review fallback path",
            SignalLevel = "error",
            PendingActions = ["Go to Review and choose rollback option"]
        };
    }


    private static string? ExtractTargetRuntimeIdFromComment(string? comment)
    {
        var explicitRuntimeId = FirstNonEmpty(
            ExtractValueFromComment(comment, "targetRuntimeId"),
            ExtractValueFromComment(comment, "targetHireId"),
            ExtractValueFromComment(comment, "hireId"));
        if (!string.IsNullOrWhiteSpace(explicitRuntimeId))
        {
            return explicitRuntimeId;
        }
        return null;
    }

    private static string ResolveTargetTemplateId(EmployeeDetailDto employee)
    {
        var directTemplateId = FirstNonEmpty(employee.SourceTemplateId, employee.BasedOnTemplateId, employee.RoleName);
        if (string.IsNullOrWhiteSpace(directTemplateId))
        {
            return "default";
        }

        var binding = ResolveFixtureTemplateBinding(directTemplateId);
        if (!string.IsNullOrWhiteSpace(binding?.FixtureTemplateId))
        {
            return binding!.FixtureTemplateId!;
        }

        return directTemplateId;
    }

    private static string? ExtractPathFromComment(string? comment)
    {
        return ExtractValueFromComment(comment, "path");
    }

    private static string BuildWorkspaceKey(string owner, string employeeId)
    {
        return $"{owner.Trim()}::{employeeId.Trim()}";
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

    private static string? ExtractValueFromComment(string? comment, string key)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var trimmed = comment.Trim();
        if (string.Equals(key, "path", StringComparison.OrdinalIgnoreCase) && Directory.Exists(trimmed))
        {
            return trimmed;
        }

        var marker = $"{key}=";
        var markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var value = trimmed[(markerIndex + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var endIndex = value.IndexOf(';');
        if (endIndex >= 0)
        {
            value = value[..endIndex];
        }

        return value.Trim().Trim('"', '\'');
    }

    private static string? NormalizeStatus(string? status, string? lifecycleStatus)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            return normalized switch
            {
                "hired" => "hired",
                "interning_ai" => "interning_ai",
                "interning_human" => "interning_human",
                "live" => "live",
                "failed" => "failed",
                "retired" => "retired",
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            return null;
        }

        var value = lifecycleStatus.Trim().ToLowerInvariant();
        if (value.Contains("failed") || value.Contains("error"))
        {
            return "failed";
        }

        if (value.Contains("ai"))
        {
            return "interning_ai";
        }

        if (value.Contains("human") || value.Contains("onboarding") || value.Contains("intern"))
        {
            return "interning_human";
        }

        if (value.Contains("live"))
        {
            return "live";
        }

        if (value.Contains("retired"))
        {
            return "retired";
        }

        return null;
    }

    private sealed record EvaluationWorkspaceContext(
        string TargetHireId,
        string TargetSandboxId,
        string EvaluatorHireId,
        string EvaluatorSandboxId,
        DateTimeOffset? SkillLoadedAtUtc,
        string? SessionId);

    private sealed record TargetArtifactWarmupResult(
        string WorkspacePath,
        string SourceArtifactPath);

    private sealed record TargetArtifactBundle(
        string FileName,
        byte[] Content,
        string Sha256,
        string SourceType,
        string SourcePath);

    private sealed record EvaluatorVerdictResult(
        bool Passed,
        string Summary,
        decimal OverallScore,
        IReadOnlyList<EvaluationDimensionScoreDto> DimensionScores,
        string RawVerdictJson);

    private sealed record TestcaseSourceFile(
        string FileName,
        string SourcePath,
        string RawJson,
        string SourceType);

    private sealed record TraceExecutionEvidence(
        string TestcaseId,
        string ScenarioName,
        string Input,
        string ExecutionId,
        string TraceJson,
        string TraceAssetUrl);

    private sealed record OntologySourceFile(
        string FileName,
        string SourcePath,
        string Content,
        string SourceType);

    private sealed record OntologyRuleCandidate(
        string Dimension,
        string Text,
        string SourceFile);

    private sealed record OntologyProfile(
        IReadOnlyDictionary<string, decimal> DimensionWeights,
        IReadOnlyList<string> DimensionRules,
        IReadOnlyList<OntologySourceFile> Sources,
        string SourceSummary);

    private sealed record FixtureTemplateBinding(
        string TemplateId,
        string? FixtureTemplateId,
        string? FixtureEmployeeId);

    private sealed record ParsedTestcase(
        string TestcaseId,
        string ScenarioName,
        string SourceFile,
        string SourcePath,
        string RawJson,
        IReadOnlyList<string> ExpectedSteps,
        string InputPrompt);
}

