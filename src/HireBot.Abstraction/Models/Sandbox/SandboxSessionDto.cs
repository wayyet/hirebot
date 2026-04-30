namespace HireBot.Abstraction.Models.Sandbox;

public sealed record SandboxSessionDto(
    Guid SessionEntityId,
    Guid? SandboxInstanceEntityId,
    string SessionId,
    string ScopeType,
    string ScopeKey,
    string SandboxRole,
    string SessionKey,
    string? ChannelId,
    string? SenderId,
    string OwnerSubject,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
