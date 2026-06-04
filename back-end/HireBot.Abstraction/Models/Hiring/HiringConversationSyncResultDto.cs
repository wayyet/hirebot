namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationSyncResultDto(
    int ExtractedFieldsCount,
    IReadOnlyList<string> ExtractedKeys);
