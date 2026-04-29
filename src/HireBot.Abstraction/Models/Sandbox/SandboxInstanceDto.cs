namespace HireBot.Abstraction.Models.Sandbox;

public sealed record SandboxInstanceDto(
    Guid InstanceId,
    string SandboxId,
    string ScopeType,
    string ScopeKey,
    string SandboxRole,
    string ProvisioningMode,
    string OwnerSubject,
    string TenantId,
    string OperatorId,
    string State,
    string? GatewayEndpoint,
    DateTimeOffset? ExpiresAtUtc,
    string? LastError,
    string? UseCase,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
