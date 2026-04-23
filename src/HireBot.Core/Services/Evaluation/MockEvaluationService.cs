using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Core.Services.Internal;

namespace HireBot.Core.Services.Evaluation;

public sealed class MockEvaluationService(
    IEvaluationScenarioProvider evaluationScenarioProvider,
    IEmployeeRuntimeStore store,
    IRequestContextService requestContextService) : IEvaluationService
{
    public async Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(400, "employeeId 不能为空");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EvaluationStateDto>.ErrorResponse(404, "员工不存在");
        }

        var state = await evaluationScenarioProvider.GetEvaluationStateAsync(employeeId.Trim(), cancellationToken);
        return ApiResponse<EvaluationStateDto>.SuccessResponse(state);
    }

    public async Task<ApiResponse<EmployeeDetailDto>> SubmitOnboardingDecisionAsync(
        string employeeId,
        EvaluationOnboardingDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null || string.IsNullOrWhiteSpace(request.Decision))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId 与 decision 为必填项");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "员工不存在");
        }

        var decision = request.Decision.Trim().ToUpperInvariant();
        var updated = decision switch
        {
            "ONBOARD" => employee with
            {
                LifecycleStatus = "实习中",
                EvalPhase = "passed",
                StageSummary = "已通过人工评估，开始实习",
                PrimarySignal = "实习中，等待积累评估数据",
                SignalLevel = "ok",
                InternshipStartAt = employee.InternshipStartAt ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
            },
            "FORCE" => employee with
            {
                LifecycleStatus = "实习中",
                EvalPhase = "passed",
                StageSummary = "已强制上岗，进入实习阶段",
                PrimarySignal = "实习中，建议重点观察",
                SignalLevel = "warn",
                InternshipStartAt = employee.InternshipStartAt ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
            },
            "REJECT" => employee with
            {
                LifecycleStatus = "待人工评估",
                EvalPhase = "pending_review",
                StageSummary = "人工评估未通过，等待重新评估",
                PrimarySignal = "待重新评估",
                SignalLevel = "warn"
            },
            _ => null
        };

        if (updated is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "decision 仅支持 ONBOARD、REJECT、FORCE");
        }

        await store.UpsertAsync(owner, updated, cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "上岗判定已提交");
    }
}
