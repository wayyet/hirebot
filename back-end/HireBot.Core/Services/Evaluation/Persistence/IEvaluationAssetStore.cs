namespace HireBot.Core.Services.Evaluation.Persistence;

internal interface IEvaluationAssetStore
{
    Task<StoredEvaluationAsset> SaveTextAsync(
        string tenantId,
        string sessionId,
        int iteration,
        string assetType,
        string fileName,
        string content,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<StoredEvaluationAsset> SaveBytesAsync(
        string tenantId,
        string sessionId,
        int iteration,
        string assetType,
        string fileName,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default);
}
