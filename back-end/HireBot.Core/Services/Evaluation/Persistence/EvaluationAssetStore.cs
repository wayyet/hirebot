using System.Security.Cryptography;
using System.Text;
using HireBot.Abstraction;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation.Persistence;

internal sealed class EvaluationAssetStore(
    IFileStore fileStore,
    ILogger<EvaluationAssetStore> logger) : IEvaluationAssetStore
{
    public async Task<StoredEvaluationAsset> SaveTextAsync(
        string sessionId,
        int iteration,
        string assetType,
        string fileName,
        string content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        return await SaveBytesAsync(
            sessionId,
            iteration,
            assetType,
            fileName,
            bytes,
            mimeType,
            cancellationToken);
    }

    public async Task<StoredEvaluationAsset> SaveBytesAsync(
        string sessionId,
        int iteration,
        string assetType,
        string fileName,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var safeSessionId = Sanitize(sessionId, "session");
        var safeAssetType = Sanitize(assetType, "asset");
        var safeFileName = BuildSafeFileName(fileName);
        var versionedFileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{safeFileName}";

        var virtualPath = $"resources/evaluation/{safeSessionId}/iter-{Math.Max(1, iteration):D2}/{safeAssetType}/{versionedFileName}";

        using var stream = new MemoryStream(content);
        var storagePath = await fileStore.SaveAsync(virtualPath, stream, cancellationToken);

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));

        // 构造公共 URL 路径（兼容当前 PhysicalFileProvider 映射 /resources → evaluationResourceRoot）
        var relativePath = virtualPath.Replace('\\', '/');
        var publicUrl = $"/{relativePath}";

        logger.LogInformation(
            "Saved evaluation asset. SessionId={SessionId}, AssetType={AssetType}, RelativePath={RelativePath}, Size={Size}",
            sessionId,
            assetType,
            relativePath,
            content.LongLength);

        return new StoredEvaluationAsset(
            RelativePath: relativePath,
            PublicUrl: publicUrl,
            MimeType: string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType.Trim(),
            Size: content.LongLength,
            ContentHash: hash,
            PhysicalPath: storagePath);
    }

    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var sanitized = new string(value.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_')
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string BuildSafeFileName(string fileName)
    {
        var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "asset.json" : fileName.Trim());
        var safeChars = safeName
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_')
            .ToArray();

        var normalized = new string(safeChars);
        return string.IsNullOrWhiteSpace(normalized) ? "asset.json" : normalized;
    }
}
