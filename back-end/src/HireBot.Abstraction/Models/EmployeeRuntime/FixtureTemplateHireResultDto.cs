namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record FixtureTemplateHireResultDto(
    string EmployeeId,
    string TemplateId,
    string InstanceType,
    string Status,
    bool CreatedByFixtureFallback);
