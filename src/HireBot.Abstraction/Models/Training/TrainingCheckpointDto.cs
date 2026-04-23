namespace HireBot.Abstraction.Models.Training;

public sealed record TrainingCheckpointDto(
    string Key,
    string Label,
    string Status,
    string? Detail);
