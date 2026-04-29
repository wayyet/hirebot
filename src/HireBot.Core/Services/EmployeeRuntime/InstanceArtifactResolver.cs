using HireBot.Repository.Entities;
using Microsoft.Extensions.Configuration;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class InstanceArtifactResolver(IConfiguration configuration) : IInstanceArtifactResolver
{
    public Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            throw new DirectoryNotFoundException($"实例五件套目录不存在: {root}");
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

    private string ResolveRoot()
    {
        var configured = configuration["HireBot:ArtifactStoreRoot"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "hirebot-artifacts"));
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
}

