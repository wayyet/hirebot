namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationResultDto(
    string HireId,
    string SessionId,
    string CurrentStage,
    bool RequiresAudit,
    HiringConversationMessageDto AssistantMessage,
    HiringStagePreviewDto LatestPreview);
