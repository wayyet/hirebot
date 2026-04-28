namespace HireBot.Core.Services.Evaluation.Persistence;

internal interface IEvaluationAssetStore
{
    Task<StoredEvaluationAsset> SaveTextAsync(
        string sessionId,
        int iteration,
        string assetType,
        string fileName,
        string content,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<StoredEvaluationAsset> SaveBytesAsync(
        string sessionId,
        int iteration,
        string assetType,
        string fileName,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default);
}
