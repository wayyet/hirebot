namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationAssetRefDto(
    string AssetType,
    string RelatedKey,
    string RelativePath,
    string PublicUrl,
    string CreatedAtUtc);
