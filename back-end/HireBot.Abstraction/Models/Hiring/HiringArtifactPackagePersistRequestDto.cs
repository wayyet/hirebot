namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringArtifactPackagePersistRequestDto(
    string HireId,
    string SessionId,
    string FileName,
    IReadOnlyDictionary<string, byte[]> Files,
    /// <summary>
    /// 本次导入的包版本 ID（调用方提供，为 null 时服务内部自动生成）。
    /// 每次导入应传入唯一值，确保多次导入不覆盖旧包。
    /// </summary>
    string? PackageId = null);
