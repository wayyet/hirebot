namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record RuntimeChatMessageDto(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record RuntimeChatRequestDto(
    string TenantId,
    string InstanceId,
    string InstanceType,
    string OwnerUserId,
    string Channel,
    string ConversationId,
    string ArtifactRoot,
    string CurrentVersion,
    IReadOnlyList<RuntimeChatMessageDto> Messages,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record RuntimeChatResponseDto(
    string Content,
    IReadOnlyDictionary<string, string?>? Metadata = null);

