using HireBot.Repository.Entities;
using Microsoft.Extensions.Configuration;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例产物解析器，根据实例类型解析产物路径和元数据。
/// </summary>
public sealed class InstanceArtifactResolver(IConfiguration configuration) : IInstanceArtifactResolver
{
    /// <summary>
    /// 解析实例的产物路径和元数据。
    /// </summary>
    /// <param name="instance">实例实体</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>产物解析结果</returns>
    public Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 根据实例类型构建产物根路径
        var root = instance.InstanceType switch
        {
            "department" => Path.Combine(
                ResolveRoot(),
                "instances",
                "department",
                Sanitize(instance.InstanceId),
                "versions",
                Sanitize(instance.CurrentVersion)),
            "personal_clone" => Path.Combine(
                ResolveRoot(),
                "instances",
                "personal_clone",
                Sanitize(instance.FromInstanceId ?? "unknown"),
                Sanitize(instance.InstanceId),
                "versions",
                Sanitize(instance.CurrentVersion)),
            "private_branch" => Path.Combine(
                ResolveRoot(),
                "instances",
                "private_branch",
                Sanitize(instance.FromInstanceId ?? "unknown"),
                Sanitize(instance.InstanceId),
                "versions",
                Sanitize(instance.CurrentVersion)),
            _ => Path.Combine(
                ResolveRoot(),
                "instances",
                Sanitize(instance.InstanceType),
                Sanitize(instance.InstanceId),
                "versions",
                Sanitize(instance.CurrentVersion))
        };

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"实例产物目录不存在: {root}");
        }

        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["artifact_root"] = root,
            ["instance_type"] = instance.InstanceType,
            ["from_instance_id"] = instance.FromInstanceId,
            ["current_version"] = instance.CurrentVersion
        };

        return Task.FromResult(new InstanceArtifactResolution(root, metadata));
    }

    /// <summary>
    /// 解析产物存储根目录。
    /// </summary>
    private string ResolveRoot()
    {
        var configured = configuration["HireBot:ArtifactStoreRoot"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "hirebot-artifacts"));
    }

    /// <summary>
    /// 清理路径中的非法字符。
    /// </summary>
    /// <param name="value">待清理的值</param>
    /// <returns>清理后的值</returns>
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