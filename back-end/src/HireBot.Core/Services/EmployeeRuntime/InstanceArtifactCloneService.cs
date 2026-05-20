using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例产物克隆服务，负责克隆员工实例的产物文件和存储部门员工产物。
/// </summary>
public sealed class InstanceArtifactCloneService(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    HireBotDbContext dbContext) : IInstanceArtifactCloneService
{
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
        if (sourceRoot is null)
        {
            throw new InvalidOperationException("源部门员工未找到可复制的实例包，请先完成雇佣交付或重新导入实例产物");
        }

        sourceRoot = ResolveCloneSourceFallback(source, sourceRoot) ?? sourceRoot;

        var version = BuildVersion();
        var targetRoot = BuildPersonalCloneVersionRoot(source.EmployeeId, targetInstanceId, version);
        Directory.CreateDirectory(targetRoot);

        var copied = CopyDirectory(sourceRoot, targetRoot, cancellationToken);
        if (copied.Count == 0)
        {
            throw new InvalidOperationException("源部门员工实例包为空，无法创建分身");
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
    /// </summary>
    private async Task<string?> ResolveSourceRootAsync(EmployeeDetailDto source, CancellationToken cancellationToken)
    {
        var currentVersion = await dbContext.Instances
            .AsNoTracking()
            .Where(item => item.InstanceId == source.EmployeeId)
            .Select(item => item.CurrentVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            var instanceRoot = BuildDepartmentVersionRoot(source.EmployeeId, currentVersion);
            if (Directory.Exists(instanceRoot))
            {
                return instanceRoot;
            }

            // For personal_clone sources, artifacts are under instances/personal_clone/{parentId}/{instanceId}/
            if (!string.IsNullOrWhiteSpace(source.FromInstanceId))
            {
                var cloneRoot = BuildCloneVersionRoot(
                    source.InstanceType ?? "personal_clone",
                    source.FromInstanceId,
                    source.EmployeeId,
                    currentVersion);
                if (Directory.Exists(cloneRoot))
                {
                    return cloneRoot;
                }
            }
        }

        var fixtureRoot = ResolveFixtureRoot(source.EmployeeId);
        if (!string.IsNullOrWhiteSpace(fixtureRoot) && Directory.Exists(fixtureRoot))
        {
            return fixtureRoot;
        }

        var digitalWorkforceRoot = Path.Combine(ResolveDigitalWorkforceRoot(), Sanitize(source.EmployeeId));
        if (Directory.Exists(digitalWorkforceRoot))
        {
            return digitalWorkforceRoot;
        }

        return null;
    }

    /// <summary>
    /// 解析克隆源的回退路径。
    /// </summary>
    private string? ResolveCloneSourceFallback(EmployeeDetailDto source, string sourceRoot)
    {
        if (!LooksLikeMetadataOnlyPackage(sourceRoot))
        {
            return null;
        }

        var templateRoot = ResolveTemplatePackageRoot(source.SourceTemplateId);
        if (!string.IsNullOrWhiteSpace(templateRoot) && Directory.Exists(templateRoot))
        {
            return templateRoot;
        }

        var basedOnRoot = ResolveTemplatePackageRoot(source.BasedOnTemplateId);
        if (!string.IsNullOrWhiteSpace(basedOnRoot) && Directory.Exists(basedOnRoot))
        {
            return basedOnRoot;
        }

        return null;
    }

    /// <summary>
    /// 解析模板包根路径。
    /// </summary>
    private string? ResolveTemplatePackageRoot(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        var configured = configuration["HireBot:TemplatePackagesRoot"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(configured.Trim(), templateId.Trim()));
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
    /// </summary>
    private static bool LooksLikeMetadataOnlyPackage(string sourceRoot)
    {
        try
        {
            var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetFileName(path))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .ToArray();

            if (files.Length == 0)
            {
                return true;
            }

            if (files.Length > 2)
            {
                return false;
            }

            return files.All(name =>
                name.Equals("instance.json", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
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
