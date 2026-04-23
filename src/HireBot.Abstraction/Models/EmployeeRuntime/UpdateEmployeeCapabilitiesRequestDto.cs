using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record UpdateEmployeeCapabilitiesRequestDto
{
    [Required]
    public IReadOnlyList<EmployeeCapabilityUpdateDto> Capabilities { get; init; } = [];
}
