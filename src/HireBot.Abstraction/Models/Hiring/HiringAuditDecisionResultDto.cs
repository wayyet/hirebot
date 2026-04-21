namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringAuditDecisionResultDto(
    string HireId,
    string Stage,
    string Decision,
    string CurrentStage,
    bool RequiresAudit,
    string CollectionPhase);
