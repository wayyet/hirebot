namespace HireBot.Core.Services.Evaluation.Persistence;

internal sealed record StoredEvaluationAsset(
    string RelativePath,
    string PublicUrl,
    string MimeType,
    long Size,
    string ContentHash,
    string PhysicalPath);
