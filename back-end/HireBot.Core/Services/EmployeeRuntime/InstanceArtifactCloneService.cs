using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例产物克隆服务，负责克隆员工实例的产物文件和存储部门员工产物。
/// </summary>
public sealed class InstanceArtifactCloneService(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    HireBotDbContext dbContext,
    IHiringFileStore hiringFileStore,
    ILogger<InstanceArtifactCloneService>? logger = null) : IInstanceArtifactCloneService
{
    // 关键产物子目录：缺失这些目录通常意味着沙箱无法完成 ontology-extraction 等下游环节
    private static readonly string[] KeyArtifactDirectories =
    [
        "ontology",
        "skills",
        "agents",
        "knowledge",
        "tools"
    ];

    /// <summary>
    /// 克隆源员工的产物到目标实例。
    /// </summary>
    /// <param name="source">源员工详情</param>
    /// <param name="targetInstanceId">目标实例ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>克隆结果</returns>
    public async Task<InstanceArtifactCloneResult> CloneArtifactsAsync(
        EmployeeDetailDto source,
        string targetInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetInstanceId))
        {
            throw new ArgumentException("targetInstanceId is required.", nameof(targetInstanceId));
        }

        var sourceRoot = await ResolveSourceRootAsync(source, cancellationToken);

        // 目录路径均未命中时，直接从雇佣文件库 ZIP 解压到 personal-clone 目标目录，
        // 无需经过 instances/ 中转。
        if (sourceRoot is null)
        {
            return await CloneFromHiringFileStoreAsync(source, targetInstanceId, cancellationToken)
                ?? throw new InvalidOperationException("源部门员工未找到可复制的实例包，请先完成雇佣交付或重新导入实例产物");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var version = BuildVersion();
        var targetRoot = BuildPersonalCloneVersionRoot(source.EmployeeId, targetInstanceId, version);
        Directory.CreateDirectory(targetRoot);

        var copied = CopyDirectory(sourceRoot, targetRoot, cancellationToken);
        if (copied.Count == 0)
        {
            throw new InvalidOperationException("源部门员工实例包为空，无法创建分身");
        }

        // 复制完成后再次校验目标目录是否包含真实产物，便于尽早暴露链路断点
        if (!HasRequiredArtifactStructure(targetRoot))
        {
            var missingKeyDirectories = GetMissingKeyDirectories(targetRoot);
            logger?.LogWarning(
                "克隆完成但目标产物目录缺少关键内容：EmployeeId={EmployeeId}, TargetInstanceId={TargetInstanceId}, TargetRoot={TargetRoot}, MissingKeyDirectories={MissingKeyDirectories}",
                source.EmployeeId,
                targetInstanceId,
                targetRoot,
                missingKeyDirectories);
        }

        await Task.CompletedTask;
        return new InstanceArtifactCloneResult(version, targetRoot, copied);
    }

    /// <summary>
    /// 存储部门员工的产物文件。
    /// </summary>
    /// <param name="departmentInstanceId">部门实例ID</param>
    /// <param name="files">文件内容字典（文件名 -> 字节数组）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存储结果</returns>
    public async Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(
        string departmentInstanceId,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(departmentInstanceId))
        {
            throw new ArgumentException("departmentInstanceId is required.", nameof(departmentInstanceId));
        }

        if (files.Count == 0)
        {
            throw new ArgumentException("files is required.", nameof(files));
        }

        var version = BuildVersion();
        var targetRoot = BuildDepartmentVersionRoot(departmentInstanceId, version);
        Directory.CreateDirectory(targetRoot);

        var copied = new List<string>();
        foreach (var pair in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeRelativePath(pair.Key);
            if (string.IsNullOrWhiteSpace(relativePath) || pair.Value.Length == 0)
            {
                continue;
            }

            var targetPath = Path.Combine(targetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            await File.WriteAllBytesAsync(targetPath, pair.Value, cancellationToken);
            copied.Add(relativePath);
        }

        if (copied.Count == 0)
        {
            throw new InvalidOperationException("交付包为空，无法保存部门员工实例包");
        }

        return new InstanceArtifactCloneResult(version, targetRoot, copied);
    }

    /// <summary>
    /// 解析源员工的产物根路径。
    /// 各分支按优先级匹配：部门版本目录 → 分身版本目录 → 示例 fixture 目录 → 数字员工目录。
    /// 命中分支若缺少关键产物结构（如 ontology/ 等），仅记录告警，不中断回退链路。
    /// </summary>
    private async Task<string?> ResolveSourceRootAsync(EmployeeDetailDto source, CancellationToken cancellationToken)
    {
        var currentVersion = await dbContext.Instances
            .AsNoTracking()
            .Where(item => item.InstanceId == source.EmployeeId)
            .Select(item => item.CurrentVersion)
            .FirstOrDefaultAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            var instanceRoot = BuildDepartmentVersionRoot(source.EmployeeId, currentVersion);
            if (Directory.Exists(instanceRoot))
            {
                WarnIfArtifactStructureIncomplete(instanceRoot, source.EmployeeId, "department-version");
                return instanceRoot;
            }

            // 对于 personal_clone 源，产物落在 instances/personal_clone/{parentId}/{instanceId}/ 下
            if (!string.IsNullOrWhiteSpace(source.FromInstanceId))
            {
                var cloneRoot = BuildCloneVersionRoot(
                    source.InstanceType ?? "personal_clone",
                    source.FromInstanceId,
                    source.EmployeeId,
                    currentVersion);
                if (Directory.Exists(cloneRoot))
                {
                    WarnIfArtifactStructureIncomplete(cloneRoot, source.EmployeeId, "clone-version");
                    return cloneRoot;
                }
            }
        }

        var fixtureRoot = ResolveFixtureRoot(source.EmployeeId);
        if (!string.IsNullOrWhiteSpace(fixtureRoot) && Directory.Exists(fixtureRoot))
        {
            WarnIfArtifactStructureIncomplete(fixtureRoot, source.EmployeeId, "fixture");
            return fixtureRoot;
        }

        var digitalWorkforceRoot = Path.Combine(ResolveDigitalWorkforceRoot(), Sanitize(source.EmployeeId));
        if (Directory.Exists(digitalWorkforceRoot))
        {
            WarnIfArtifactStructureIncomplete(digitalWorkforceRoot, source.EmployeeId, "digital-workforce");
            return digitalWorkforceRoot;
        }

        logger?.LogWarning(
            "未能为员工解析到任何产物根目录：EmployeeId={EmployeeId}, CurrentVersion={CurrentVersion}, FromInstanceId={FromInstanceId}",
            source.EmployeeId,
            currentVersion,
            source.FromInstanceId);
        return null;
    }

    /// <summary>
    /// 解析示例实例根路径。
    /// </summary>
    private string? ResolveFixtureRoot(string employeeId)
    {
        var configuredRoot = HireBotPathResolver.ResolveInstanceFixturesRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:InstanceFixturesRoot"]);
        var candidates = new[]
        {
            configuredRoot,
            HireBotPathResolver.ResolveConventionalInstanceFixturesRoot()
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Cast<string>();

        foreach (var root in candidates.Where(Directory.Exists))
        {
            foreach (var directory in Directory.GetDirectories(root))
            {
                var instancePath = Path.Combine(directory, "instance.json");
                if (!File.Exists(instancePath))
                {
                    continue;
                }

                var content = File.ReadAllText(instancePath);
                if (content.Contains($"\"employeeId\": \"{employeeId}\"", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains($"\"employeeId\":\"{employeeId}\"", StringComparison.OrdinalIgnoreCase))
                {
                    return directory;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 构建部门版本根路径。
    /// </summary>
    private string BuildDepartmentVersionRoot(string instanceId, string version)
    {
        return Path.Combine(ResolveRoot(), "instances", "department", Sanitize(instanceId), "versions", Sanitize(version));
    }

    /// <summary>
    /// 构建分身/私有分支版本的根路径（用于读取已有产物）。
    /// </summary>
    private string BuildCloneVersionRoot(string instanceType, string fromInstanceId, string instanceId, string version)
    {
        var typeSegment = instanceType switch
        {
            "personal_clone" => "personal_clone",
            "private_branch" => "personal_clone",
            _ => "personal_clone"
        };
        return Path.Combine(
            ResolveRoot(),
            "instances",
            typeSegment,
            Sanitize(fromInstanceId),
            Sanitize(instanceId),
            "versions",
            Sanitize(version));
    }

    /// <summary>
    /// 构建个人分身版本根路径。
    /// </summary>
    private string BuildPersonalCloneVersionRoot(string sourceDepartmentInstanceId, string cloneInstanceId, string version)
    {
        return Path.Combine(
            ResolvePersonalCloneArtifactsRoot(),
            Sanitize(sourceDepartmentInstanceId),
            Sanitize(cloneInstanceId),
            "versions",
            Sanitize(version));
    }

    private string ResolvePersonalCloneArtifactsRoot()
    {
        return HireBotPathResolver.ResolvePersonalCloneArtifactsRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:PersonalCloneArtifactsRoot"]);
    }

    private string ResolveDigitalWorkforceRoot()
    {
        return HireBotPathResolver.ResolveDigitalWorkforceRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:DigitalWorkforceRoot"]);
    }

    /// <summary>
    /// 解析存储根目录。
    /// </summary>
    private string ResolveRoot()
    {
        return HireBotPathResolver.ResolveArtifactStoreRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:ArtifactStoreRoot"]);
    }

    /// <summary>
    /// 复制目录内容。
    /// </summary>
    private static List<string> CopyDirectory(string sourceRoot, string targetRoot, CancellationToken cancellationToken)
    {
        var copied = new List<string>();
        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath).Replace('\\', '/');
            relativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var targetPath = Path.Combine(targetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            copied.Add(relativePath);
        }

        return copied;
    }

    /// <summary>
    /// 判断是否仅包含元数据的包。
    /// 通过 <see cref="HasRequiredArtifactStructure"/> 反向校验：缺少实质产物（子目录或非元数据文件）时视为元数据包，
    /// 从而触发模板回退。
    /// </summary>
    private static bool LooksLikeMetadataOnlyPackage(string sourceRoot)
    {
        return !HasRequiredArtifactStructure(sourceRoot);
    }

    /// <summary>
    /// 验证目录是否包含除元数据文件以外的实际产物内容。
    /// 判定标准：存在任意包含文件的子目录，或顶层存在 instance.json/manifest.json 之外的文件。
    /// 用于尽早识别 ontology/ 等关键目录缺失导致的链路断点。
    /// </summary>
    private static bool HasRequiredArtifactStructure(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return false;
        }

        try
        {
            // 任一子目录中存在文件，即视为存在实际产物内容
            foreach (var directory in Directory.EnumerateDirectories(rootPath))
            {
                if (Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any())
                {
                    return true;
                }
            }

            // 顶层除 instance.json / manifest.json 以外的任何文件，也算具备实质内容
            foreach (var filePath in Directory.EnumerateFiles(rootPath))
            {
                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                if (!fileName.Equals("instance.json", StringComparison.OrdinalIgnoreCase) &&
                    !fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // 枚举异常视为结构不完整，交由调用方触发回退或告警
            return false;
        }
    }

    /// <summary>
    /// 返回当前目录中缺失的关键子目录名（用于结构化日志）。
    /// </summary>
    private static IReadOnlyList<string> GetMissingKeyDirectories(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return KeyArtifactDirectories;
        }

        var missing = new List<string>(KeyArtifactDirectories.Length);
        foreach (var name in KeyArtifactDirectories)
        {
            var path = Path.Combine(rootPath, name);
            if (!Directory.Exists(path))
            {
                missing.Add(name);
            }
        }

        return missing;
    }

    /// <summary>
    /// 校验目标目录是否包含关键产物子目录，缺失时记录结构化告警。
    /// 不抛出异常，避免破坏既有回退链路。
    /// </summary>
    private void WarnIfArtifactStructureIncomplete(string rootPath, string employeeId, string source)
    {
        if (logger is null)
        {
            return;
        }

        var missing = GetMissingKeyDirectories(rootPath);
        // ontology/ 是 ontology-extraction 等下游环节最关键的依赖，单独高亮告警
        var ontologyMissing = missing.Contains("ontology", StringComparer.OrdinalIgnoreCase);

        if (ontologyMissing)
        {
            logger.LogWarning(
                "产物根目录缺少 ontology/ 关键目录，可能导致沙箱 ontology-extraction 失败：EmployeeId={EmployeeId}, Source={Source}, RootPath={RootPath}, MissingKeyDirectories={MissingKeyDirectories}",
                employeeId,
                source,
                rootPath,
                missing);
            return;
        }

        if (!HasRequiredArtifactStructure(rootPath))
        {
            logger.LogWarning(
                "产物根目录缺少实质内容（仅含元数据或为空）：EmployeeId={EmployeeId}, Source={Source}, RootPath={RootPath}",
                employeeId,
                source,
                rootPath);
        }
    }

    /// <summary>
    /// 规范化相对路径。
    /// </summary>
    private static string NormalizeRelativePath(string path)
    {
        var segments = path
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".."))
        {
            return string.Empty;
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// 构建版本号。
    /// </summary>
    private static string BuildVersion()
    {
        return $"v_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    }

    /// <summary>
    /// 当 ResolveSourceRootAsync 未命中任何目录时的兜底路径：
    /// 直接从雇佣文件库（artifact-store/{tenantId}/{hireId}/{packageId}/package.zip）
    /// 解压到 personal-clone-artifacts/{sourceId}/{cloneId}/versions/{ver}/，
    /// 无需经过 instances/ 中转目录。
    /// </summary>
    private async Task<InstanceArtifactCloneResult?> CloneFromHiringFileStoreAsync(
        EmployeeDetailDto source,
        string targetInstanceId,
        CancellationToken cancellationToken)
    {
        var instanceRecord = await dbContext.Instances
            .AsNoTracking()
            .Where(i => i.InstanceId == source.EmployeeId)
            .Select(i => new { i.HireId, i.FinalPackageId, i.TenantId })
            .FirstOrDefaultAsync(cancellationToken);

        if (instanceRecord is null
            || string.IsNullOrWhiteSpace(instanceRecord.HireId)
            || string.IsNullOrWhiteSpace(instanceRecord.FinalPackageId))
        {
            logger?.LogDebug(
                "CloneFromHiringFileStore: 实例无 HireId/FinalPackageId，无法从文件库克隆: EmployeeId={EmployeeId}",
                source.EmployeeId);
            return null;
        }

        var tenantId = string.IsNullOrWhiteSpace(instanceRecord.TenantId) ? "default" : instanceRecord.TenantId;

        if (!await hiringFileStore.FinalPackageExistsAsync(
                tenantId, instanceRecord.HireId, instanceRecord.FinalPackageId, cancellationToken))
        {
            logger?.LogWarning(
                "CloneFromHiringFileStore: 雇佣文件库中未找到候选包: EmployeeId={EmployeeId}, TenantId={TenantId}, HireId={HireId}, FinalPackageId={FinalPackageId}",
                source.EmployeeId, tenantId, instanceRecord.HireId, instanceRecord.FinalPackageId);
            return null;
        }

        var version = BuildVersion();
        var targetRoot = BuildPersonalCloneVersionRoot(source.EmployeeId, targetInstanceId, version);
        Directory.CreateDirectory(targetRoot);

        var copied = new List<string>();
        try
        {
            await using var zipStream = await hiringFileStore.OpenFinalPackageAsync(
                tenantId, instanceRecord.HireId, instanceRecord.FinalPackageId, cancellationToken);

            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var relativePath = NormalizeRelativePath(entry.FullName);
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                var targetPath = Path.Combine(targetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                await using var entryStream = entry.Open();
                await using var fileStream = new FileStream(
                    targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true);
                await entryStream.CopyToAsync(fileStream, cancellationToken);
                copied.Add(relativePath);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "CloneFromHiringFileStore: 解压候选包失败: EmployeeId={EmployeeId}, TargetRoot={TargetRoot}",
                source.EmployeeId, targetRoot);
            try { Directory.Delete(targetRoot, recursive: true); } catch { /* best-effort */ }
            return null;
        }

        if (copied.Count == 0)
        {
            Directory.Delete(targetRoot, recursive: true);
            logger?.LogWarning(
                "CloneFromHiringFileStore: 解压后目标目录为空: EmployeeId={EmployeeId}",
                source.EmployeeId);
            return null;
        }

        WarnIfArtifactStructureIncomplete(targetRoot, source.EmployeeId, "hiring-file-store");

        logger?.LogInformation(
            "CloneFromHiringFileStore: 已从雇佣文件库直接克隆到 personal-clone-artifacts: EmployeeId={EmployeeId}, Version={Version}, FileCount={FileCount}",
            source.EmployeeId, version, copied.Count);

        return new InstanceArtifactCloneResult(version, targetRoot, copied);
    }

    /// <summary>
    /// 清理路径中的非法字符。
    /// </summary>
    private static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }

        return trimmed.Length == 0 ? "unknown" : trimmed;
    }
}
