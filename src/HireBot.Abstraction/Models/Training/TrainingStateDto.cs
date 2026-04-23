namespace HireBot.Abstraction.Models.Training;

public sealed record TrainingStateDto(
    string EmployeeId,
    string Phase,
    int EvolutionRound,
    decimal ExamScore,
    bool AiPassed,
    bool RequiresHumanReview,
    IReadOnlyList<TrainingCheckpointDto> Checkpoints);
