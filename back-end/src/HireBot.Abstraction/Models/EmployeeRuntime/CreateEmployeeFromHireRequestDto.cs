namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record CreateEmployeeFromHireRequestDto(
    string HireId,
    string TemplateId,
    string TemplateName,
    string OwnerSubject,
    string TenantId,
    string OperatorId,
    IReadOnlyList<string> Capabilities);
