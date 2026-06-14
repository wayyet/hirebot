using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class InstanceArtifactCloneService(
    HireBotDbContext dbContext,
    IFileStore fileStore,
    ILogger<InstanceArtifactCloneService>? logger = null) : IInstanceArtifactCloneService
{
    private static readonly string[] KeyArtifactDirectories =
    ["ontology", "skills", "agents", "knowledge", "tools"];

    public async Task<InstanceArtifactCloneResult> CloneArtifactsAsync(
        EmployeeDetailDto source, string targetInstanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetInstanceId))
            throw new ArgumentException("targetInstanceId is required.", nameof(targetInstanceId));

        var sourcePrefix = await ResolveSourcePrefixAsync(source, cancellationToken);
        if (sourcePrefix is null)
        {
            return await CloneFromHiringFileStoreAsync(source, targetInstanceId, cancellationToken)
                ?? throw new InvalidOperationException("源部门员工未找到可复制的实例包");
        }

        var version = BuildVersion();
        var targetPrefix = BuildPersonalCloneVersionPrefix(source.EmployeeId, targetInstanceId, version);
        var copied = await CopyFilesAsync(sourcePrefix, targetPrefix, cancellationToken);
        if (copied.Count == 0) throw new InvalidOperationException("源部门员工实例包为空，无法创建分身");

        await WarnIfArtifactStructureIncompleteAsync(targetPrefix, source.EmployeeId, "clone", cancellationToken);
        return new InstanceArtifactCloneResult(version, targetPrefix, copied);
    }

    public async Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(
        string departmentInstanceId, IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(departmentInstanceId))
            throw new ArgumentException("departmentInstanceId is required.", nameof(departmentInstanceId));
        if (files.Count == 0) throw new ArgumentException("files is required.", nameof(files));

        var version = BuildVersion();
        var targetPrefix = BuildDepartmentVersionPrefix(departmentInstanceId, version);
        var copied = new List<string>();
        foreach (var pair in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeRelativePath(pair.Key);
            if (string.IsNullOrWhiteSpace(relativePath) || pair.Value.Length == 0) continue;
            var virtualPath = $"{targetPrefix}/{relativePath}";
            using var ms = new MemoryStream(pair.Value);
            await fileStore.SaveAsync(virtualPath, ms, cancellationToken);
            copied.Add(relativePath);
        }
        if (copied.Count == 0) throw new InvalidOperationException("交付包为空，无法保存部门员工实例包");
        return new InstanceArtifactCloneResult(version, targetPrefix, copied);
    }

    private async Task<string?> ResolveSourcePrefixAsync(EmployeeDetailDto source, CancellationToken cancellationToken)
    {
        var currentVersion = await dbContext.Instances.AsNoTracking()
            .Where(i => i.InstanceId == source.EmployeeId).Select(i => i.CurrentVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            var deptPrefix = BuildDepartmentVersionPrefix(source.EmployeeId, currentVersion);
            if (await PrefixExistsAsync(deptPrefix, cancellationToken)) return deptPrefix;
            if (!string.IsNullOrWhiteSpace(source.FromInstanceId))
            {
                var clonePrefix = BuildCloneVersionPrefix(source.InstanceType ?? "personal_clone", source.FromInstanceId, source.EmployeeId, currentVersion);
                if (await PrefixExistsAsync(clonePrefix, cancellationToken)) return clonePrefix;
            }
        }
        var dwPrefix = $"digital-workforce/{Sanitize(source.EmployeeId)}";
        if (await PrefixExistsAsync(dwPrefix, cancellationToken)) return dwPrefix;
        logger?.LogWarning("未解析到产物源: EmployeeId={EmployeeId}", source.EmployeeId);
        return null;
    }

    private async Task<bool> PrefixExistsAsync(string prefix, CancellationToken ct) =>
        (await fileStore.ListAsync(prefix, ct)).Count > 0;

    private async Task<IReadOnlyList<string>> CopyFilesAsync(string srcPrefix, string dstPrefix, CancellationToken ct)
    {
        var entries = await fileStore.ListAsync(srcPrefix, ct);
        var copied = new List<string>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var rel = entry.Path;
            if (rel.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)) rel = rel[srcPrefix.Length..].TrimStart('/');
            rel = NormalizeRelativePath(rel);
            if (string.IsNullOrWhiteSpace(rel)) continue;
            await using var s = await fileStore.OpenReadAsync(entry.Path, ct);
            await fileStore.SaveAsync($"{dstPrefix}/{rel}", s, ct);
            copied.Add(rel);
        }
        return copied;
    }

    private async Task<bool> HasRequiredArtifactStructureAsync(string prefix, CancellationToken ct)
    {
        var entries = await fileStore.ListAsync(prefix, ct);
        if (entries.Count == 0) return false;
        foreach (var e in entries)
        {
            var rel = e.Path; if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) rel = rel[prefix.Length..].TrimStart('/');
            var si = rel.IndexOf('/');
            if (si > 0) { if (!IsMetadataFile(rel[..si])) return true; }
            else if (!IsMetadataFile(rel)) return true;
        }
        return false;
    }

    private static bool IsMetadataFile(string name) =>
        name.Equals("instance.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals("describe.md", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<string>> GetMissingKeyDirectoriesAsync(string prefix, CancellationToken ct)
    {
        var entries = await fileStore.ListAsync(prefix, ct);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var rel = e.Path; if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) rel = rel[prefix.Length..].TrimStart('/');
            var si = rel.IndexOf('/'); if (si > 0) dirs.Add(rel[..si]);
        }
        return KeyArtifactDirectories.Where(d => !dirs.Contains(d)).ToArray();
    }

    private async Task WarnIfArtifactStructureIncompleteAsync(string prefix, string employeeId, string source, CancellationToken ct)
    {
        if (logger is null) return;
        var missing = await GetMissingKeyDirectoriesAsync(prefix, ct);
        if (missing.Contains("ontology", StringComparer.OrdinalIgnoreCase))
        { logger.LogWarning("产物缺少 ontology/: EmployeeId={EmployeeId} Source={Source}", employeeId, source); return; }
        if (!await HasRequiredArtifactStructureAsync(prefix, ct))
            logger.LogWarning("产物缺少实质内容: EmployeeId={EmployeeId} Source={Source}", employeeId, source);
    }

    private async Task<InstanceArtifactCloneResult?> CloneFromHiringFileStoreAsync(EmployeeDetailDto source, string targetInstanceId, CancellationToken ct)
    {
        var record = await dbContext.Instances.AsNoTracking()
            .Where(i => i.InstanceId == source.EmployeeId).Select(i => new { i.HireId, i.FinalPackageId, i.TenantId })
            .FirstOrDefaultAsync(ct);
        if (record is null || string.IsNullOrWhiteSpace(record.HireId) || string.IsNullOrWhiteSpace(record.FinalPackageId)) return null;
        var tenantId = string.IsNullOrWhiteSpace(record.TenantId) ? "default" : record.TenantId;
        var zipPath = $"artifact-store/{tenantId}/{record.HireId}/{record.FinalPackageId}/package.zip";
        if (!await fileStore.ExistsAsync(zipPath, ct)) return null;

        var version = BuildVersion();
        var targetPrefix = BuildPersonalCloneVersionPrefix(source.EmployeeId, targetInstanceId, version);
        var copied = new List<string>();
        try
        {
            await using var zipStream = await fileStore.OpenReadAsync(zipPath, ct);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var rel = NormalizeRelativePath(entry.FullName); if (string.IsNullOrWhiteSpace(rel)) continue;
                await using var es = entry.Open();
                await fileStore.SaveAsync($"{targetPrefix}/{rel}", es, ct);
                copied.Add(rel);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "从雇佣文件库克隆失败: EmployeeId={EmployeeId}", source.EmployeeId);
            var cleanup = await fileStore.ListAsync(targetPrefix, ct);
            foreach (var e in cleanup) try { await fileStore.DeleteAsync(e.Path, ct); } catch { }
            return null;
        }
        if (copied.Count == 0) return null;
        await WarnIfArtifactStructureIncompleteAsync(targetPrefix, source.EmployeeId, "hiring-file-store", ct);
        return new InstanceArtifactCloneResult(version, targetPrefix, copied);
    }

    private static string BuildDepartmentVersionPrefix(string id, string v) => $"artifact-store/instances/department/{Sanitize(id)}/versions/{Sanitize(v)}";
    private static string BuildCloneVersionPrefix(string type, string fromId, string id, string v) => $"artifact-store/instances/personal_clone/{Sanitize(fromId)}/{Sanitize(id)}/versions/{Sanitize(v)}";
    private static string BuildPersonalCloneVersionPrefix(string srcId, string cloneId, string v) => $"personal-clone-artifacts/{Sanitize(srcId)}/{Sanitize(cloneId)}/versions/{Sanitize(v)}";

    private static string NormalizeRelativePath(string path) {
        var segs = path.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segs.Length == 0 || segs.Any(s => s is "." or "..")) return "";
        return string.Join('/', segs);
    }
    private static string BuildVersion() => $"v_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    private static string Sanitize(string v) { var chars = v.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray(); return chars.Length == 0 ? "unknown" : new string(chars); }
}
