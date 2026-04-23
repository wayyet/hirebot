using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record EmployeeCapabilityUpdateDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    public bool Ready { get; init; }
}
