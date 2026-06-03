namespace HireBot.Abstraction.Models.Sandbox;

public sealed record SandboxAttachmentUploadResultDto(
    Guid AssetEntityId,
    Guid? SandboxInstanceEntityId,
    Guid? SandboxSessionEntityId,
    string MediaId,
    string Url,
    string FileName,
    string MimeType,
    long SizeBytes,
    string? ContentHash,
    string? StoragePath,
    string Marker,
    DateTimeOffset CreatedAtUtc);
