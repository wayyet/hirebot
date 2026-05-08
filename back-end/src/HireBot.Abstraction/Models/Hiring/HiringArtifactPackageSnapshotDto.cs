namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringArtifactPackageSnapshotDto(
    string HireId,
    string SessionId,
    string Kind,
    string FileName,
    string LogicalPath,
    string Sha256,
    byte[] Content,
    bool IsFinal);
