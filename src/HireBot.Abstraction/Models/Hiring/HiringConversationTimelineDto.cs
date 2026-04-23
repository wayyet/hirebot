namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationTimelineDto(
    string HireId,
    string SessionId,
    string CurrentStage,
    bool RequiresAudit,
    string CollectionPhase,
    IReadOnlyList<HiringConversationMessageDto> Messages,
    IReadOnlyList<StageSkillMappingDto> StageSkills);
