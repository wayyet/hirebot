using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Abstraction.Models.Sandbox;

public sealed record SandboxCreateRequestDto
{
    public required string ScopeType { get; init; }
    public required string ScopeKey { get; init; }
    public required string SandboxRole { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public string ProvisioningMode { get; init; } = "managed";
    public string? UseCase { get; init; }
    public string? TemplateId { get; init; }
}

public sealed record SandboxRegisterRequestDto
{
    public required string SandboxId { get; init; }
    public required string ScopeType { get; init; }
    public required string ScopeKey { get; init; }
    public required string SandboxRole { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public string ProvisioningMode { get; init; } = "external";
    public string State { get; init; } = "ready";
    public string? GatewayEndpoint { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? UseCase { get; init; }
    public string? TemplateId { get; init; }
}

public sealed record SandboxInstanceLookupRequestDto
{
    public string? SandboxId { get; init; }
    public string? ScopeType { get; init; }
    public string? ScopeKey { get; init; }
    public string? SandboxRole { get; init; }
    public string? OwnerSubject { get; init; }
    public string? TenantId { get; init; }
    public string? OperatorId { get; init; }
    public string? UseCase { get; init; }
    public string? TemplateId { get; init; }
}

public sealed record SandboxEnsureSessionRequestDto
{
    public required string ScopeType { get; init; }
    public required string ScopeKey { get; init; }
    public required string SandboxRole { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public string SessionKey { get; init; } = "default";
    public string? SessionId { get; init; }
    public string? SandboxId { get; init; }
}

public sealed record SandboxSendMessageRequestDto
{
    public required string ScopeType { get; init; }
    public required string ScopeKey { get; init; }
    public required string SandboxRole { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public string SessionKey { get; init; } = "default";
    public string? SandboxId { get; init; }
    public string Content { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string>? StructuredAnswers { get; init; }
    public IReadOnlyList<HiringConversationMaterialDto>? Materials { get; init; }
    public bool UploadMaterialsAsAttachments { get; init; } = true;
}

public sealed record SandboxTimelineRequestDto
{
    public required string ScopeType { get; init; }
    public required string ScopeKey { get; init; }
    public required string SandboxRole { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public string SessionKey { get; init; } = "default";
    public string? SandboxId { get; init; }
}

public sealed record SandboxSessionDetailRequestDto
{
    public required string ScopeType { get; init; }
    public required string ScopeKey { get; init; }
    public required string SandboxRole { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public string SessionKey { get; init; } = "default";
    public string? SandboxId { get; init; }
}

public sealed record SandboxAttachmentUploadRequestDto
{
    public required string ScopeType { get; init; }
    public required string ScopeKey { get; init; }
    public required string SandboxRole { get; init; }
    public required string OwnerSubject { get; init; }
    public required string TenantId { get; init; }
    public required string OperatorId { get; init; }
    public string SessionKey { get; init; } = "default";
    public string? SandboxId { get; init; }
    public required HiringConversationMaterialDto Material { get; init; }
}

public sealed record SkillPackageUploadRequestDto
{
    public required string SandboxId { get; init; }
    public required string OwnerSubject { get; init; }
    public required byte[] ArchiveBytes { get; init; }
    public required string FileName { get; init; }
}

public sealed record SkillPackageUploadResultDto(
    bool Success,
    string? Error,
    int SkillsInstalled);
