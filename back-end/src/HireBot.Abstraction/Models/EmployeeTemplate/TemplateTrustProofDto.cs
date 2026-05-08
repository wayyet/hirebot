namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record TemplateTrustProofDto(
    int HiredCount,
    decimal SuccessRate,
    decimal AvgRating);
