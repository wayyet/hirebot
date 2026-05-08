using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Training;

namespace HireBot.Abstraction.Services.Training;

public interface ITrainingService
{
    Task<ApiResponse<TrainingStateDto>> GetTrainingStateAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> SubmitTrainingDecisionAsync(string employeeId, TrainingDecisionRequestDto request, CancellationToken cancellationToken = default);
}
