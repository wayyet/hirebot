namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringFinalizeResultDto(
    string HireId,
    string CurrentStage,
    string CollectionPhase,
    IReadOnlyList<string> GeneratedFiles,
    string DownloadUrl);
