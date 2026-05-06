using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Abstraction.Models.Sandbox;

public sealed record SandboxSessionDetailDto(
    string SessionId,
    IReadOnlyList<HiringConversationMessageDto> Messages,
    IReadOnlyList<SandboxSessionTodoItemDto> TodoItems,
    bool IsActive);

public sealed record SandboxSessionTodoItemDto(
    string Id,
    string Text,
    string? Notes,
    bool Completed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
