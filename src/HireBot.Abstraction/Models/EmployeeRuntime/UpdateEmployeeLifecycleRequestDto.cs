using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record UpdateEmployeeLifecycleRequestDto
{
    [Required]
    public string LifecycleStatus { get; init; } = string.Empty;

    public string? StageSummary { get; init; }

    public string? PrimarySignal { get; init; }

    public string? SignalLevel { get; init; }

    public string? InternshipStartAt { get; init; }

    public string? GraduatedAt { get; init; }
}
