using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Training;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class UnavailableEvaluationScenarioProvider : IEvaluationScenarioProvider
{
    private const string Message = "训练/评估状态未接入真实评估数据源，Mock 数据已移除。";

    public Task<TrainingStateDto> GetTrainingStateAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }

    public Task<EvaluationStateDto> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }
}
