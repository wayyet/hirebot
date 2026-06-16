using System.IO.Compression;
using System.Security.Cryptography;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring.Artifacts;

internal sealed class HiringArtifactPackageService(
    HireBotDbContext dbContext,
    IFileStore fileStore,
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
            packageId: null,
            cancellationToken);
    }

    public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(
        HiringArtifactPackagePersistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // 每次导入使用唯一 packageId，确保多次导入不互相覆盖；调用方也可自行提供以实现幂等性
        var packageId = !string.IsNullOrWhiteSpace(request.PackageId)
            ? request.PackageId.Trim()
            : Guid.NewGuid().ToString("N");
        return PersistPackageAsync(
            request,
            HiringArtifactPackageKinds.FinalPackageZip,
            category: $"packages/final/{packageId}",
            logicalPath: $"packages/final/{packageId}/package.zip",
            isFinal: true,
            packageId: packageId,
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

    public async Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageByEmployeeIdAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return null;
        }

        var normalizedEmployeeId = employeeId.Trim();

        // employeeId → Instance.(HireId, FinalPackageId, TenantId) → 直接拼语义路径读文件
        // 无需 HiringArtifacts 关联查询
        var instance = await dbContext.Instances
            .AsNoTracking()
            .Where(i => i.InstanceId == normalizedEmployeeId)
            .Select(i => new { i.HireId, i.FinalPackageId, i.TenantId })
            .FirstOrDefaultAsync(cancellationToken);

        if (instance is not null
            && !string.IsNullOrWhiteSpace(instance.HireId)
            && !string.IsNullOrWhiteSpace(instance.FinalPackageId))
        {
            var tenantId = string.IsNullOrWhiteSpace(instance.TenantId) ? "default" : instance.TenantId;
            // 优先读取新的扁平 .zip 路径，兼容旧的 package.zip 目录结构
            var newPackagePath = ArtifactStoragePaths.BuildFinalPackagePath(tenantId, instance.HireId, instance.FinalPackageId);
            var legacyPackagePath = $"artifact-store/{tenantId}/{instance.HireId}/{instance.FinalPackageId}/package.zip";

            var packagePath = await fileStore.ExistsAsync(newPackagePath, cancellationToken)
                ? newPackagePath
                : await fileStore.ExistsAsync(legacyPackagePath, cancellationToken)
                    ? legacyPackagePath
                    : null;

            if (packagePath is not null)
            {
                await using var stream = await fileStore.OpenReadAsync(packagePath, cancellationToken);
                using var mem = new MemoryStream();
                await stream.CopyToAsync(mem, cancellationToken);
                var content = mem.ToArray();
                return new HiringArtifactPackageSnapshotDto(
                    HireId: instance.HireId,
                    SessionId: string.Empty,
                    Kind: HiringArtifactPackageKinds.FinalPackageZip,
                    FileName: ArtifactStoragePaths.ExtractDownloadFileName(packagePath),
                    LogicalPath: $"packages/final/{instance.FinalPackageId}/package.zip",
                    Sha256: Convert.ToHexStringLower(SHA256.HashData(content)),
                    Content: content,
                    IsFinal: true);
            }
        }

        // FinalPackageId 未设置（旧数据/首次导入前）：退化到按时间取最新包
        if (!string.IsNullOrWhiteSpace(instance?.HireId))
        {
            return await GetLatestPackageAsync(instance.HireId, cancellationToken);
        }

        // 兼容最旧数据：HireId 未写入实例时，通过 HiringStructuredData 反向索引兜底
        var record = await dbContext.HiringStructuredData
            .AsNoTracking()
            .Where(d => d.FieldKey == "linked_employee_id" && d.FieldValue == normalizedEmployeeId)
            .OrderByDescending(d => d.CollectedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(record?.HireId))
        {
            return await GetLatestPackageAsync(record.HireId, cancellationToken);
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
        string? packageId,
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

        // 最终包使用扁平路径: artifact-store/{tenant}/{hireId}/{hireId}-{packageId前8}.zip
        // 中间包沿用 sessions/{sessionId}/{category}/package.zip 旧路径
        await using var archiveStream = new MemoryStream(archive, writable: false);
        string storagePath;
        string effectiveLogicalPath;
        HiringArtifactEntity entity;
        if (isFinal && packageId is not null)
        {
            var tenantId = session.TenantId ?? "default";
            storagePath = await fileStore.SaveAsync(
                ArtifactStoragePaths.BuildFinalPackagePath(tenantId, normalizedHireId, packageId),
                archiveStream,
                cancellationToken);
            effectiveLogicalPath = $"packages/final/{packageId}/package.zip";
        }
        else
        {
            var tenantId = session.TenantId ?? "default";
            storagePath = await fileStore.SaveAsync(
                ArtifactStoragePaths.BuildIntermediatePackagePath(tenantId, normalizedSessionId, category),
                archiveStream,
                cancellationToken);
            effectiveLogicalPath = logicalPath;
        }

        // 最终包：每次新增一条记录（不做 upsert），旧包可审计
        // 中间包：按 sessionId+kind+logicalPath 做 upsert（保持原有行为）
        if (isFinal && packageId is not null)
        {
            entity = new HiringArtifactEntity
            {
                SessionId = normalizedSessionId,
                Kind = kind,
                LogicalPath = effectiveLogicalPath,
                FileName = normalizedFileName,
                SizeBytes = archive.LongLength,
                Sha256 = sha256,
                PackageId = packageId,
                StoragePath = storagePath,
                IsFinal = isFinal,
                IsArchived = false,
                UploadedAtUtc = uploadedAtUtc
            };
            dbContext.HiringArtifacts.Add(entity);
        }
        else
        {
            entity = (await dbContext.HiringArtifacts
                .FirstOrDefaultAsync(
                    item => item.SessionId == normalizedSessionId &&
                            item.Kind == kind &&
                            item.LogicalPath == effectiveLogicalPath &&
                            item.DeletedAtUtc == null,
                    cancellationToken))!;

            if (entity is null)
            {
                entity = new HiringArtifactEntity
                {
                    SessionId = normalizedSessionId,
                    Kind = kind,
                    LogicalPath = effectiveLogicalPath,
                    FileName = normalizedFileName,
                    SizeBytes = archive.LongLength,
                    Sha256 = sha256,
                    PackageId = packageId,
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
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Persisted hiring artifact package. HireId={HireId}, SessionId={SessionId}, Kind={Kind}, PackageId={PackageId}, FileCount={FileCount}, Sha256={Sha256}",
            normalizedHireId,
            normalizedSessionId,
            kind,
            packageId ?? "(none)",
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

        if (!await fileStore.ExistsAsync(entity.StoragePath, cancellationToken))
        {
            throw new InvalidOperationException(
                $"artifact package file missing on disk: {entity.StoragePath}");
        }

        await using var stream = await fileStore.OpenReadAsync(entity.StoragePath, cancellationToken);
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
