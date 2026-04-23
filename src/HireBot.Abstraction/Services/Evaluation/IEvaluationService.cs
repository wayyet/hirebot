using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;

namespace HireBot.Abstraction.Services.Evaluation;

public interface IEvaluationService
{
    Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> SubmitOnboardingDecisionAsync(string employeeId, EvaluationOnboardingDecisionRequestDto request, CancellationToken cancellationToken = default);
}
