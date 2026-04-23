namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record TemplatePrerequisiteDto(
    string SystemName,
    string PermissionName,
    string RequiredLevel,
    string Purpose);
