namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record EmployeeTemplateListDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<EmployeeTemplateCardDto> Items);
