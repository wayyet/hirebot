namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringStageCompletionDto(
    string Stage,
    int RequiredFieldCount,
    int SatisfiedFieldCount,
    decimal CompletionRate,
    IReadOnlyList<string> SatisfiedFields,
    IReadOnlyList<string> BlockingFields,
    bool ReadyForNextStage);
