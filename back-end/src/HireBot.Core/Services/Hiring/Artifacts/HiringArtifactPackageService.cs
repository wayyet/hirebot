using System.IO.Compression;
using System.Security.Cryptography;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring.Artifacts;

internal sealed class HiringArtifactPackageService(
    HireBotDbContext dbContext,
    IHiringFileStore hiringFileStore,
    ILogger<HiringArtifactPackageService> logger) : IHiringArtifactPackageService
{
    private const string IntermediateCategory = "packages/intermediate";
    private const string FinalCategory = "packages/final";
    private const string PackageStorageFileName = "package.zip";

    public Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(
        HiringArtifactPackagePersistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PersistPackageAsync(
            request,
            HiringArtifactPackageKinds.IntermediatePackageZip,
            IntermediateCategory,
            logicalPath: "packages/intermediate/package.zip",
            isFinal: false,
            cancellationToken);
    }

    public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(
        HiringArtifactPackagePersistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PersistPackageAsync(
            request,
            HiringArtifactPackageKinds.FinalPackageZip,
            FinalCategory,
            logicalPath: "packages/final/package.zip",
            isFinal: true,
            cancellationToken);
    }

    public Task<HiringArtifactPackageSnapshotDto?> GetPackageByKindAsync(
        string hireId,
        string kind,
        CancellationToken cancellationToken = default)
    {
        return GetPackageByKindInternalAsync(hireId, kind, cancellationToken);
    }

    public async Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var normalizedHireId = NormalizeRequired(hireId, nameof(hireId));

        foreach (var kind in new[]
                 {
                     HiringArtifactPackageKinds.FinalPackageZip,
                     HiringArtifactPackageKinds.IntermediatePackageZip
                 })
        {
            var artifactEntity = await FindLatestArtifactEntityForHireAsync(
                normalizedHireId,
                kind,
                cancellationToken);
            if (artifactEntity is null)
            {
                continue;
            }

            var snapshot = await LoadPackageSnapshotAsync(
                normalizedHireId,
                artifactEntity.SessionId,
                kind,
                cancellationToken);
            if (snapshot is not null)
            {
                return snapshot;
            }
        }

        return null;
    }

    public async Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId))
        {
            return HiringArtifactDownloadResult.Error(400, "hireId cannot be empty");
        }

        var snapshot = await GetPackageByKindInternalAsync(
            hireId,
            HiringArtifactPackageKinds.FinalPackageZip,
            cancellationToken);
        if (snapshot is null)
        {
            return HiringArtifactDownloadResult.Error(409, "交付包尚未生成，请先执行 finalize");
        }

        return HiringArtifactDownloadResult.Success(
            snapshot.FileName,
            "application/zip",
            snapshot.Content);
    }

    public async Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(
        string hireId,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId))
        {
            return HiringArtifactDownloadResult.Error(400, "hireId cannot be empty");
        }

        if (!TryNormalizeArtifactPath(artifactName, out var normalizedArtifactPath, out var error))
        {
            return HiringArtifactDownloadResult.Error(400, error);
        }

        var snapshot = await GetPackageByKindInternalAsync(
            hireId,
            HiringArtifactPackageKinds.FinalPackageZip,
            cancellationToken);
        if (snapshot is null)
        {
            return HiringArtifactDownloadResult.Error(409, "交付物尚未生成，请先执行 finalize");
        }

        var content = ExtractEntry(snapshot.Content, normalizedArtifactPath);
        if (content is null || content.Length == 0)
        {
            return HiringArtifactDownloadResult.NotFound("交付物不存在");
        }

        return HiringArtifactDownloadResult.Success(
            Path.GetFileName(normalizedArtifactPath),
            ResolveArtifactContentType(normalizedArtifactPath),
            content);
    }

    private async Task<HiringArtifactPackageSnapshotDto> PersistPackageAsync(
        HiringArtifactPackagePersistRequestDto request,
        string kind,
        string category,
        string logicalPath,
        bool isFinal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedHireId = NormalizeRequired(request.HireId, nameof(request.HireId));
        var normalizedSessionId = NormalizeRequired(request.SessionId, nameof(request.SessionId));
        var normalizedFileName = NormalizeRequired(request.FileName, nameof(request.FileName));
        var normalizedFiles = NormalizePackageFiles(request.Files);
        if (normalizedFiles.Count == 0)
        {
            throw new InvalidOperationException("artifact package must contain at least one file.");
        }

        var session = await dbContext.HiringSessions
            .FirstOrDefaultAsync(
                item => item.HireId == normalizedHireId &&
                        item.SessionId == normalizedSessionId &&
                        item.DeletedAtUtc == null,
                cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"hiring session not found for hireId '{normalizedHireId}' and sessionId '{normalizedSessionId}'.");
        }

        var archive = BuildArchive(normalizedFiles);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(archive));
        var uploadedAtUtc = DateTimeOffset.UtcNow;

        await using var archiveStream = new MemoryStream(archive, writable: false);
        var storagePath = await hiringFileStore.SaveAsync(
            normalizedSessionId,
            category,
            PackageStorageFileName,
            archiveStream,
            cancellationToken);

        var entity = await dbContext.HiringArtifacts
            .FirstOrDefaultAsync(
                item => item.SessionId == normalizedSessionId &&
                        item.Kind == kind &&
                        item.LogicalPath == logicalPath &&
                        item.DeletedAtUtc == null,
                cancellationToken);

        if (entity is null)
        {
            entity = new HiringArtifactEntity
            {
                SessionId = normalizedSessionId,
                Kind = kind,
                LogicalPath = logicalPath,
                FileName = normalizedFileName,
                SizeBytes = archive.LongLength,
                Sha256 = sha256,
                StoragePath = storagePath,
                IsFinal = isFinal,
                IsArchived = false,
                UploadedAtUtc = uploadedAtUtc
            };
            dbContext.HiringArtifacts.Add(entity);
        }
        else
        {
            entity.FileName = normalizedFileName;
            entity.SizeBytes = archive.LongLength;
            entity.Sha256 = sha256;
            entity.StoragePath = storagePath;
            entity.IsFinal = isFinal;
            entity.IsArchived = false;
            entity.UploadedAtUtc = uploadedAtUtc;
            entity.DeletedAtUtc = null;
            entity.DeletedBy = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Persisted hiring artifact package. HireId={HireId}, SessionId={SessionId}, Kind={Kind}, FileCount={FileCount}, Sha256={Sha256}",
            normalizedHireId,
            normalizedSessionId,
            kind,
            normalizedFiles.Count,
            sha256);

        return new HiringArtifactPackageSnapshotDto(
            normalizedHireId,
            normalizedSessionId,
            kind,
            normalizedFileName,
            logicalPath,
            sha256,
            archive,
            isFinal);
    }

    private async Task<HiringArtifactPackageSnapshotDto?> GetPackageByKindInternalAsync(
        string hireId,
        string kind,
        CancellationToken cancellationToken)
    {
        var normalizedHireId = NormalizeRequired(hireId, nameof(hireId));
        var artifactEntity = await FindLatestArtifactEntityForHireAsync(
            normalizedHireId,
            kind,
            cancellationToken);
        if (artifactEntity is null)
        {
            return null;
        }

        return await LoadPackageSnapshotAsync(
            normalizedHireId,
            artifactEntity.SessionId,
            kind,
            cancellationToken);
    }

    /// <summary>
    /// 按 hireId 查找指定 kind 的最新 artifact，避免仅依赖 HiringSessions 首条记录导致取错 session。
    /// </summary>
    private async Task<HiringArtifactEntity?> FindLatestArtifactEntityForHireAsync(
        string hireId,
        string kind,
        CancellationToken cancellationToken)
    {
        return await (
            from artifact in dbContext.HiringArtifacts.AsNoTracking()
            join session in dbContext.HiringSessions.AsNoTracking()
                on artifact.SessionId equals session.SessionId
            where session.HireId == hireId
                  && session.DeletedAtUtc == null
                  && artifact.Kind == kind
                  && artifact.DeletedAtUtc == null
                  && !artifact.IsArchived
            orderby artifact.UploadedAtUtc descending
            select artifact)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<HiringArtifactPackageSnapshotDto?> LoadPackageSnapshotAsync(
        string hireId,
        string sessionId,
        string kind,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.HiringArtifacts
            .AsNoTracking()
            .Where(item =>
                item.SessionId == sessionId &&
                item.Kind == kind &&
                item.DeletedAtUtc == null &&
                !item.IsArchived)
            .OrderByDescending(item => item.UploadedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!await hiringFileStore.ExistsAsync(entity.StoragePath, cancellationToken))
        {
            throw new InvalidOperationException(
                $"artifact package file missing on disk: {entity.StoragePath}");
        }

        await using var stream = await hiringFileStore.OpenReadAsync(entity.StoragePath, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        return new HiringArtifactPackageSnapshotDto(
            hireId,
            sessionId,
            entity.Kind,
            entity.FileName,
            entity.LogicalPath,
            entity.Sha256,
            memory.ToArray(),
            entity.IsFinal);
    }

    private static IReadOnlyDictionary<string, byte[]> NormalizePackageFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var normalized = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in files)
        {
            if (!TryNormalizeArtifactPath(pair.Key, out var normalizedPath, out var error))
            {
                throw new InvalidOperationException($"artifact path '{pair.Key}' is invalid: {error}");
            }

            normalized[normalizedPath] = pair.Value ?? [];
        }

        return normalized;
    }

    private static byte[] BuildArchive(IReadOnlyDictionary<string, byte[]> files)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(pair.Value, 0, pair.Value.Length);
            }
        }

        return memoryStream.ToArray();
    }

    private static byte[]? ExtractEntry(byte[] archiveBytes, string normalizedArtifactPath)
    {
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.FirstOrDefault(item =>
            string.Equals(
                item.FullName.Replace('\\', '/'),
                normalizedArtifactPath,
                StringComparison.OrdinalIgnoreCase));
        if (entry is null || string.IsNullOrWhiteSpace(entry.Name))
        {
            return null;
        }

        using var entryStream = entry.Open();
        using var memory = new MemoryStream();
        entryStream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }

    private static bool TryNormalizeArtifactPath(string artifactPath, out string normalizedArtifactPath, out string error)
    {
        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName cannot be empty";
            return false;
        }

        var segments = artifactPath
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName is invalid";
            return false;
        }

        if (segments.Any(static segment =>
                segment == "." ||
                segment == ".." ||
                segment.Contains(':', StringComparison.Ordinal) ||
                segment.Contains('\0', StringComparison.Ordinal)))
        {
            normalizedArtifactPath = string.Empty;
            error = "artifactName contains invalid path segments";
            return false;
        }

        normalizedArtifactPath = string.Join('/', segments);
        error = string.Empty;
        return true;
    }

    private static string ResolveArtifactContentType(string artifactPath)
    {
        var extension = Path.GetExtension(artifactPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return extension.ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".md" => "text/markdown; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".yaml" or ".yml" => "application/yaml",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }
}
