using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例产物克隆服务。所有 artifact 统一存储为单个 .zip 文件，路径包含租户隔离。
/// </summary>
public sealed class InstanceArtifactCloneService(
    HireBotDbContext dbContext,
    IFileStore fileStore,
    ILogger<InstanceArtifactCloneService>? logger = null) : IInstanceArtifactCloneService
{
    public async Task<InstanceArtifactCloneResult> CloneArtifactsAsync(
        EmployeeDetailDto source, string targetInstanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetInstanceId))
            throw new ArgumentException("targetInstanceId is required.", nameof(targetInstanceId));

        var tenantId = await ResolveTenantAsync(source.EmployeeId, cancellationToken);
        var sourcePath = await ResolveSourceZipPathAsync(source, tenantId, cancellationToken);
        if (sourcePath is null)
        {
            return await CloneFromHiringFileStoreAsync(source, targetInstanceId, cancellationToken)
                ?? throw new InvalidOperationException("源部门员工未找到可复制的实例包");
        }

        var version = BuildVersion();
        var targetPath = ArtifactStoragePaths.BuildPersonalCloneArtifactsPath(
            tenantId, source.EmployeeId, targetInstanceId, version);
        await CopyZipAsync(sourcePath, targetPath, cancellationToken);

        return new InstanceArtifactCloneResult(version, targetPath,
            [ArtifactStoragePaths.ExtractDownloadFileName(targetPath)]);
    }

    public async Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(
        string departmentInstanceId, IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(departmentInstanceId))
            throw new ArgumentException("departmentInstanceId is required.", nameof(departmentInstanceId));
        if (files.Count == 0) throw new ArgumentException("files is required.", nameof(files));

        var tenantId = await ResolveTenantAsync(departmentInstanceId, cancellationToken);
        var version = BuildVersion();
        var targetPath = ArtifactStoragePaths.BuildDepartmentVersionPath(tenantId, departmentInstanceId, version);

        var zipBytes = BuildZipArchive(files);
        using var ms = new MemoryStream(zipBytes, writable: false);
        await fileStore.SaveAsync(targetPath, ms, cancellationToken);

        return new InstanceArtifactCloneResult(version, targetPath,
            [ArtifactStoragePaths.ExtractDownloadFileName(targetPath)]);
    }

    /// <summary>查询实例所属租户</summary>
    private async Task<string> ResolveTenantAsync(string employeeId, CancellationToken ct)
    {
        var tenant = await dbContext.Instances.AsNoTracking()
            .Where(i => i.InstanceId == employeeId)
            .Select(i => i.TenantId)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(tenant) ? "default" : tenant;
    }

    /// <summary>在多个候选路径中查找源的 .zip 文件（兼容旧散文件目录）</summary>
    private async Task<string?> ResolveSourceZipPathAsync(
        EmployeeDetailDto source, string tenantId, CancellationToken cancellationToken)
    {
        var currentVersion = await dbContext.Instances.AsNoTracking()
            .Where(i => i.InstanceId == source.EmployeeId).Select(i => i.CurrentVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            var deptPath = ArtifactStoragePaths.BuildDepartmentVersionPath(tenantId, source.EmployeeId, currentVersion);
            if (await fileStore.ExistsAsync(deptPath, cancellationToken)) return deptPath;

            // 兼容旧散文件目录（无租户前缀）
            var legacyDeptPrefix = $"artifact-store/instances/department/{Sanitize(source.EmployeeId)}/versions/{Sanitize(currentVersion)}";
            if (await PrefixExistsAsync(legacyDeptPrefix, cancellationToken)) return await MigrateDirectoryToZipAsync(legacyDeptPrefix, deptPath, cancellationToken);

            if (!string.IsNullOrWhiteSpace(source.FromInstanceId))
            {
                var clonePath = ArtifactStoragePaths.BuildPersonalCloneVersionPath(
                    tenantId, source.FromInstanceId, source.EmployeeId, currentVersion);
                if (await fileStore.ExistsAsync(clonePath, cancellationToken)) return clonePath;

                var legacyClonePrefix = $"artifact-store/instances/personal_clone/{Sanitize(source.FromInstanceId)}/{Sanitize(source.EmployeeId)}/versions/{Sanitize(currentVersion)}";
                if (await PrefixExistsAsync(legacyClonePrefix, cancellationToken)) return await MigrateDirectoryToZipAsync(legacyClonePrefix, clonePath, cancellationToken);
            }
        }

        var dwPath = ArtifactStoragePaths.BuildDigitalWorkforcePath(tenantId, source.EmployeeId);
        if (await fileStore.ExistsAsync(dwPath, cancellationToken)) return dwPath;

        var legacyDwPrefix = $"digital-workforce/{Sanitize(source.EmployeeId)}";
        if (await PrefixExistsAsync(legacyDwPrefix, cancellationToken)) return await MigrateDirectoryToZipAsync(legacyDwPrefix, dwPath, cancellationToken);

        logger?.LogWarning("未解析到产物源: EmployeeId={EmployeeId}", source.EmployeeId);
        return null;
    }

    private async Task<InstanceArtifactCloneResult?> CloneFromHiringFileStoreAsync(
        EmployeeDetailDto source, string targetInstanceId, CancellationToken ct)
    {
        var record = await dbContext.Instances.AsNoTracking()
            .Where(i => i.InstanceId == source.EmployeeId)
            .Select(i => new { i.HireId, i.FinalPackageId, i.TenantId })
            .FirstOrDefaultAsync(ct);
        if (record is null || string.IsNullOrWhiteSpace(record.HireId) || string.IsNullOrWhiteSpace(record.FinalPackageId))
            return null;

        var tenantId = string.IsNullOrWhiteSpace(record.TenantId) ? "default" : record.TenantId;
        var newPath = ArtifactStoragePaths.BuildFinalPackagePath(tenantId, record.HireId, record.FinalPackageId);
        var legacyPath = $"artifact-store/{tenantId}/{record.HireId}/{record.FinalPackageId}/package.zip";

        var sourcePath = await fileStore.ExistsAsync(newPath, ct)
            ? newPath
            : await fileStore.ExistsAsync(legacyPath, ct)
                ? legacyPath
                : null;

        if (sourcePath is null) return null;

        var version = BuildVersion();
        var targetPath = ArtifactStoragePaths.BuildPersonalCloneArtifactsPath(
            tenantId, source.EmployeeId, targetInstanceId, version);

        await CopyZipAsync(sourcePath, targetPath, ct);
        return new InstanceArtifactCloneResult(version, targetPath,
            [ArtifactStoragePaths.ExtractDownloadFileName(targetPath)]);
    }

    private async Task CopyZipAsync(string sourcePath, string targetPath, CancellationToken ct)
    {
        try
        {
            await using var sourceStream = await fileStore.OpenReadAsync(sourcePath, ct);
            await fileStore.SaveAsync(targetPath, sourceStream, ct);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "复制 ZIP 失败: Source={Source} Target={Target}", sourcePath, targetPath);
            try { await fileStore.DeleteAsync(targetPath, ct); } catch { }
            throw;
        }
    }

    private async Task<string> MigrateDirectoryToZipAsync(
        string legacyDirPrefix, string newZipPath, CancellationToken ct)
    {
        var entries = await fileStore.ListAsync(legacyDirPrefix, ct);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var rel = entry.Path;
            if (rel.StartsWith(legacyDirPrefix, StringComparison.OrdinalIgnoreCase))
                rel = rel[legacyDirPrefix.Length..].TrimStart('/');
            if (string.IsNullOrWhiteSpace(rel)) continue;

            await using var s = await fileStore.OpenReadAsync(entry.Path, ct);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct);
            files[rel] = ms.ToArray();
        }

        if (files.Count == 0)
            throw new InvalidOperationException($"旧目录为空，无法迁移: {legacyDirPrefix}");

        var zipBytes = BuildZipArchive(files);
        using var zipMs = new MemoryStream(zipBytes, writable: false);
        await fileStore.SaveAsync(newZipPath, zipMs, ct);

        foreach (var entry in entries)
        {
            try { await fileStore.DeleteAsync(entry.Path, ct); } catch { }
        }

        logger?.LogInformation(
            "旧散文件目录已迁移为 ZIP: OldPrefix={OldPrefix} NewPath={NewPath} FileCount={Count}",
            legacyDirPrefix, newZipPath, files.Count);

        return newZipPath;
    }

    private static byte[] BuildZipArchive(IReadOnlyDictionary<string, byte[]> files)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(pair.Value, 0, pair.Value.Length);
            }
        }
        return ms.ToArray();
    }

    private async Task<bool> PrefixExistsAsync(string prefix, CancellationToken ct) =>
        (await fileStore.ListAsync(prefix, ct)).Count > 0;

    private static string BuildVersion() => $"v_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    private static string Sanitize(string v) => ArtifactStoragePaths.Sanitize(v);
}
