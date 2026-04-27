using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Core.Services.Internal;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

public sealed class MockEvaluationService(
    IEvaluationScenarioProvider evaluationScenarioProvider,
    IEmployeeRuntimeStore store,
    IEmployeeHiringService employeeHiringService,
    IRequestContextService requestContextService,
    ILogger<MockEvaluationService> logger) : IEvaluationService
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

    public async Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(400, "employeeId cannot be empty");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(404, "employee not found");
        }

        var state = await evaluationScenarioProvider.GetEvaluationStateAsync(employeeId.Trim(), cancellationToken);
        return ApiResponse<EvaluationStateDto>.SuccessResponse(state);
    }

    public async Task<ApiResponse<EvaluationSandboxConversationStateDto>> GetEvaluationSandboxConversationAsync(
        string employeeId,
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

        var workspaceResult = await EnsureWorkspaceReadyAsync(owner, employee, null, null, cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionResult = await EnsureEvaluatorConversationStartedAsync(workspaceResult.Data, cancellationToken);
        if (!sessionResult.Success || sessionResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(sessionResult.Code, sessionResult.Message);
        }

        var timelineResult = await employeeHiringService.GetConversationTimelineAsync(workspaceResult.Data.EvaluatorHireId, cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        var refreshedWorkspace = workspaceResult.Data with { SessionId = timelineResult.Data.SessionId };
        EvaluationWorkspaces[BuildWorkspaceKey(owner, employee.EmployeeId)] = refreshedWorkspace;

        return ApiResponse<EvaluationSandboxConversationStateDto>.SuccessResponse(
            BuildSandboxConversationState(employee, refreshedWorkspace, timelineResult.Data));
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

        var workspaceResult = await EnsureWorkspaceReadyAsync(owner, employee, request.SkillRootPath, null, cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
        }

        var sessionResult = await EnsureEvaluatorConversationStartedAsync(workspaceResult.Data, cancellationToken);
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
        var sendResult = await employeeHiringService.SendConversationMessageAsync(
            workspaceResult.Data.EvaluatorHireId,
            sendRequest,
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        var timelineResult = await employeeHiringService.GetConversationTimelineAsync(workspaceResult.Data.EvaluatorHireId, cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluationSandboxConversationStateDto>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        var refreshedWorkspace = workspaceResult.Data with { SessionId = timelineResult.Data.SessionId };
        EvaluationWorkspaces[BuildWorkspaceKey(owner, employee.EmployeeId)] = refreshedWorkspace;

        return ApiResponse<EvaluationSandboxConversationStateDto>.SuccessResponse(
            BuildSandboxConversationState(employee, refreshedWorkspace, timelineResult.Data),
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

        switch (decision)
        {
            case "START":
            {
                if (currentStatus is not ("hired" or "failed" or "interning_ai"))
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, $"current status does not allow START: {currentStatus}");
                }

                updated = employee with
                {
                    Status = "interning_ai",
                    LifecycleStatus = "AI evaluation",
                    EvalPhase = "pending_skill_upload",
                    StageSummary = "AI evaluation started, waiting for evaluation skill load",
                    PrimarySignal = "Pending action: load evaluation skill",
                    SignalLevel = "warn",
                    PendingActions = ["Load evaluation skill", "Submit AI evaluation verdict"]
                };
                message = "AI evaluation started";
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
                var workspaceResult = await EnsureWorkspaceReadyAsync(owner, employee, skillRootPath, request.Comment, cancellationToken);
                if (!workspaceResult.Success || workspaceResult.Data is null)
                {
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(workspaceResult.Code, workspaceResult.Message);
                }

                var workspaceContext = workspaceResult.Data;

                var capabilities = MergeEvaluationCapabilities(employee.Capabilities, EvaluationSkillNames);
                var configured = capabilities.Count > 0 && capabilities.All(item => item.Ready);

                updated = employee with
                {
                    Status = "interning_ai",
                    LifecycleStatus = "AI evaluation",
                    EvalPhase = "ai_running",
                    StageSummary = $"Evaluation skill loaded. evalHireId={workspaceContext.EvaluatorHireId}, evalSandboxId={workspaceContext.EvaluatorSandboxId}, targetHireId={workspaceContext.TargetHireId}, targetSandboxId={workspaceContext.TargetSandboxId}",
                    PrimarySignal = "Double-sandbox evaluation environment is ready",
                    SignalLevel = "ok",
                    PendingActions = ["Run evaluation scenarios in evaluator sandbox", "Chat with evaluation sandbox"],
                    Capabilities = capabilities,
                    IsConfigured = configured
                };
                message = "Evaluation skill loaded and evaluator workspace is ready";
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
                    return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "LOAD_SKILL must be completed before RUN");
                }

                var workspaceResult = await EnsureWorkspaceReadyAsync(owner, employee, null, request.Comment, cancellationToken);
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

    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureWorkspaceReadyAsync(
        string owner,
        EmployeeDetailDto employee,
        string? skillRootPath,
        string? comment,
        CancellationToken cancellationToken)
    {
        var workspaceKey = BuildWorkspaceKey(owner, employee.EmployeeId);
        var targetHireId = ExtractValueFromComment(comment, "hireId");
        if (string.IsNullOrWhiteSpace(targetHireId) &&
            TargetHireBindings.TryGetValue(workspaceKey, out var boundTargetHireId))
        {
            targetHireId = boundTargetHireId;
        }

        if (string.IsNullOrWhiteSpace(targetHireId))
        {
            targetHireId = ResolveHireId(employee, comment);
        }

        if (string.IsNullOrWhiteSpace(targetHireId))
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(422, "cannot resolve target hireId");
        }

        var targetStatusResult = await employeeHiringService.GetHiringStatusAsync(targetHireId, cancellationToken);
        if ((!targetStatusResult.Success || targetStatusResult.Data is null) && targetStatusResult.Code == 404)
        {
            targetStatusResult = ApiResponse<HiringStatusDto>.SuccessResponse(
                BuildFallbackTargetStatus(targetHireId, employee.EmployeeId),
                "target hire flow does not exist, use fixture fallback target sandbox");
            logger.LogWarning(
                "Target hire flow not found. Falling back to fixture target sandbox. Owner={Owner}, EmployeeId={EmployeeId}, TargetHireId={TargetHireId}",
                owner,
                employee.EmployeeId,
                targetHireId);
        }

        if (!targetStatusResult.Success || targetStatusResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(
                targetStatusResult.Code,
                $"failed to read target sandbox info: {targetStatusResult.Message}");
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

    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureEvaluatorConversationStartedAsync(
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(workspaceContext.SessionId))
        {
            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext);
        }

        var startResult = await employeeHiringService.StartConversationAsync(workspaceContext.EvaluatorHireId, cancellationToken);
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

    private async Task<ApiResponse<EvaluatorVerdictResult>> RunAiEvaluationAsync(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var startResult = await EnsureEvaluatorConversationStartedAsync(workspaceContext, cancellationToken);
        if (!startResult.Success || startResult.Data is null)
        {
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(startResult.Code, startResult.Message);
        }

        var prompt = $@"请作为评估沙箱执行本轮双沙箱评估。
目标员工：{employee.Nickname}（{employee.EmployeeId}）
目标雇佣流程：{workspaceContext.TargetHireId}
目标沙箱：{workspaceContext.TargetSandboxId}

请结合当前可访问信息给出本轮结论，并严格返回 JSON：
{{
  ""verdict"": ""PASS 或 FAIL"",
  ""summary"": ""一句话总结"",
  ""confidence"": 0 到 1 的数字
}}";

        var sendResult = await employeeHiringService.SendConversationMessageAsync(
            workspaceContext.EvaluatorHireId,
            new HiringConversationMessageRequestDto
            {
                Content = prompt
            },
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(
                sendResult.Code,
                $"failed to run evaluator sandbox: {sendResult.Message}");
        }

        var assistantContent = sendResult.Data.AssistantMessage.Content;
        var verdict = ParseSandboxVerdict(assistantContent);
        if (verdict is null)
        {
            return ApiResponse<EvaluatorVerdictResult>.ErrorResponse(
                422,
                "evaluator sandbox did not return recognizable verdict");
        }

        return ApiResponse<EvaluatorVerdictResult>.SuccessResponse(verdict);
    }

    private static EvaluatorVerdictResult? ParseSandboxVerdict(string? assistantContent)
    {
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            return null;
        }

        var trimmed = assistantContent.Trim();
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
                        return new EvaluatorVerdictResult(
                            Passed: passed,
                            Summary: summary ?? verdictValue ?? (passed ? "PASS" : "FAIL"));
                    }
                }
            }
            catch
            {
                // fallback to keyword parse
            }
        }

        if (trimmed.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("合格", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("通过", StringComparison.OrdinalIgnoreCase))
        {
            return new EvaluatorVerdictResult(Passed: true, Summary: trimmed);
        }

        if (trimmed.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("不合格", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("未通过", StringComparison.OrdinalIgnoreCase))
        {
            return new EvaluatorVerdictResult(Passed: false, Summary: trimmed);
        }

        return null;
    }

    private static EvaluationSandboxConversationStateDto BuildSandboxConversationState(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        HiringConversationTimelineDto timeline)
    {
        return new EvaluationSandboxConversationStateDto(
            EmployeeId: employee.EmployeeId,
            EvalPhase: string.IsNullOrWhiteSpace(employee.EvalPhase) ? "pending_skill_upload" : employee.EvalPhase,
            TargetHireId: workspaceContext.TargetHireId,
            TargetSandboxId: workspaceContext.TargetSandboxId,
            EvaluatorHireId: workspaceContext.EvaluatorHireId,
            EvaluatorSandboxId: workspaceContext.EvaluatorSandboxId,
            SessionId: timeline.SessionId,
            SkillLoadedAtUtc: workspaceContext.SkillLoadedAtUtc,
            Messages: timeline.Messages);
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

    private static bool IsAiEvaluationPassed(EvaluationStateDto state)
    {
        if (state.Scenarios.Count > 0 && state.Scenarios.All(item => string.Equals(item.Verdict, "passed", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(state.OverallStatus, "passed", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveHireId(EmployeeDetailDto employee, string? comment)
    {
        var explicitHireId = ExtractValueFromComment(comment, "hireId");
        if (!string.IsNullOrWhiteSpace(explicitHireId))
        {
            return explicitHireId;
        }

        if (employee.EmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
        {
            return $"hire_{employee.EmployeeId[2..]}";
        }
        if (employee.EmployeeId.StartsWith("e", StringComparison.OrdinalIgnoreCase))
        {
            return $"hire_{employee.EmployeeId}";
        }

        if (!string.IsNullOrWhiteSpace(employee.FromInstanceId) &&
            employee.FromInstanceId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
        {
            return $"hire_{employee.FromInstanceId[2..]}";
        }
        if (!string.IsNullOrWhiteSpace(employee.FromInstanceId) &&
            employee.FromInstanceId.StartsWith("e", StringComparison.OrdinalIgnoreCase))
        {
            return $"hire_{employee.FromInstanceId}";
        }

        return null;
    }

    private static HiringStatusDto BuildFallbackTargetStatus(string targetHireId, string employeeId)
    {
        var seed = new string(
            employeeId
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray())
            .Trim('_');

        if (seed.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
        {
            seed = seed[2..];
        }

        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = "fixture_target";
        }

        var sandboxId = $"sandbox_target_{seed}";
        return new HiringStatusDto(
            HireId: targetHireId,
            SandboxId: sandboxId,
            Status: "active",
            ErrorCode: null,
            ErrorMessage: null,
            CollectionPhase: "ready",
            CurrentStage: "evaluation");
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

    private sealed record EvaluatorVerdictResult(
        bool Passed,
        string Summary);
}
