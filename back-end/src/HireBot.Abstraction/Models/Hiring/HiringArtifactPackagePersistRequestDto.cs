namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringArtifactPackagePersistRequestDto(
    string HireId,
    string SessionId,
    string FileName,
    IReadOnlyDictionary<string, byte[]> Files);
