using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Training;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Training;
using HireBot.Core.Services.Internal;

namespace HireBot.Core.Services.Training;

public sealed class MockTrainingService(
    IEvaluationScenarioProvider evaluationScenarioProvider,
    IEmployeeRuntimeStore store,
    IRequestContextService requestContextService) : ITrainingService
{
    public async Task<ApiResponse<TrainingStateDto>> GetTrainingStateAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<TrainingStateDto>.ErrorResponse(400, "employeeId 不能为空");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<TrainingStateDto>.ErrorResponse(404, "员工不存在");
        }

        var state = await evaluationScenarioProvider.GetTrainingStateAsync(employeeId.Trim(), cancellationToken);
        return ApiResponse<TrainingStateDto>.SuccessResponse(state);
    }

    public async Task<ApiResponse<EmployeeDetailDto>> SubmitTrainingDecisionAsync(
        string employeeId,
        TrainingDecisionRequestDto request,
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
            "APPROVE" => employee with
            {
                Status = "interning_human",
                LifecycleStatus = "实习中",
                StageSummary = "已完成训练考核，进入实习阶段",
                PrimarySignal = "实习中，积累评估数据",
                SignalLevel = "ok",
                InternshipStartAt = employee.InternshipStartAt ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
            },
            "REJECT" => employee with
            {
                Status = "interning_ai",
                LifecycleStatus = "待AI评估",
                StageSummary = "训练未通过，等待补充材料",
                PrimarySignal = "待重新训练",
                SignalLevel = "warn"
            },
            _ => null
        };

        if (updated is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "decision 仅支持 APPROVE 或 REJECT");
        }

        await store.UpsertAsync(owner, updated, cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "训练决策已提交");
    }
}
