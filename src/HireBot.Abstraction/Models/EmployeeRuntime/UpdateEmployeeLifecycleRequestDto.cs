namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record UpdateEmployeeLifecycleRequestDto
{
    public string? Status { get; init; }

    public string? LifecycleStatus { get; init; }

    public string? StageSummary { get; init; }

    public string? PrimarySignal { get; init; }

    public string? SignalLevel { get; init; }

    public string? InternshipStartAt { get; init; }

    public string? GraduatedAt { get; init; }
}
