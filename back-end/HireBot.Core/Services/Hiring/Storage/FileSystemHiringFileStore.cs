using System.Security.Cryptography;
using HireBot.Core.Services.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Services.Hiring.Storage;

public sealed class FileSystemHiringFileStore(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment) : IHiringFileStore
{
    private string ResolveRoot()
    {
        return HireBotPathResolver.ResolveArtifactStoreRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:ArtifactStoreRoot"]);
    }

    public async Task<string> SaveAsync(
        string sessionId,
        string category,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId is required.", nameof(sessionId));
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is required.", nameof(fileName));

        var safeSession = Sanitize(sessionId);
        var safeCategory = SanitizePath(category);
        var safeName = SanitizeFileName(fileName);

        var root = ResolveRoot();
        var dir = Path.Combine(root, "sessions", safeSession, safeCategory);
        Directory.CreateDirectory(dir);

        var targetPath = Path.Combine(dir, safeName);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true);
        await content.CopyToAsync(target, cancellationToken);

        return targetPath;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) throw new ArgumentException("storagePath is required.", nameof(storagePath));
        Stream stream = new FileStream(storagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) return Task.FromResult(false);
        return Task.FromResult(File.Exists(storagePath));
    }

    public async Task<string> SaveFinalPackageAsync(
        string tenantId,
        string hireId,
        string packageId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(hireId)) throw new ArgumentException("hireId is required.", nameof(hireId));
        if (string.IsNullOrWhiteSpace(packageId)) throw new ArgumentException("packageId is required.", nameof(packageId));

        var dir = Path.Combine(ResolveRoot(), Sanitize(tenantId), Sanitize(hireId), Sanitize(packageId));
        Directory.CreateDirectory(dir);

        var targetPath = Path.Combine(dir, "package.zip");
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true);
        await content.CopyToAsync(target, cancellationToken);

        return targetPath;
    }

    public Task<Stream> OpenFinalPackageAsync(
        string tenantId,
        string hireId,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveFinalPackagePath(tenantId, hireId, packageId);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> FinalPackageExistsAsync(
        string tenantId,
        string hireId,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(hireId) || string.IsNullOrWhiteSpace(packageId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(File.Exists(ResolveFinalPackagePath(tenantId, hireId, packageId)));
    }

    private string ResolveFinalPackagePath(string tenantId, string hireId, string packageId)
    {
        return Path.Combine(ResolveRoot(), Sanitize(tenantId), Sanitize(hireId), Sanitize(packageId), "package.zip");
    }

    private static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }
        return trimmed.Length == 0 ? "unknown" : trimmed;
    }

    private static string SanitizePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim().Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var safeSegments = segments.Select(Sanitize).ToArray();
        return safeSegments.Length == 0 ? "unknown" : Path.Combine(safeSegments);
    }

    private static string SanitizeFileName(string value)
    {
        var trimmed = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }
        return trimmed.Length == 0 ? "artifact.bin" : trimmed;
    }

    internal static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

