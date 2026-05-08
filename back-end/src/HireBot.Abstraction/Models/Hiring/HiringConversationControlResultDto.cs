namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationControlResultDto(
    string HireId,
    string CurrentStage,
    string CollectionPhase,
    bool IsConversationPaused,
    bool IsConversationResponding);
