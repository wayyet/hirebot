using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Training;

namespace HireBot.Abstraction.Providers;

public interface IEvaluationScenarioProvider
{
    Task<TrainingStateDto> GetTrainingStateAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<EvaluationStateDto> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default);
}
