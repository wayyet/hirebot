namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record TemplateResponsibilityBoundaryDto(
    IReadOnlyList<string> InScope,
    IReadOnlyList<string> OutOfScope);
