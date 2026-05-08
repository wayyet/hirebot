namespace HireBot.Abstraction.Models.Hiring;

public sealed record StartHiringConversationResultDto(
    string HireId,
    string SessionId,
    string CurrentStage,
    bool RequiresAudit,
    IReadOnlyList<StageSkillMappingDto> StageSkills,
    bool IsConversationPaused = false,
    bool IsConversationResponding = false);
