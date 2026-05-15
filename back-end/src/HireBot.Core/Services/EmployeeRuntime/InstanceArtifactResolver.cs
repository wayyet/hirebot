using HireBot.Core.Services.Internal;
using HireBot.Repository.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// Resolves the artifact root used to package an instance into a runtime sandbox.
/// </summary>
public sealed class InstanceArtifactResolver(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment) : IInstanceArtifactResolver
{
    public Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = BuildCandidateRoots(instance).ToArray();
        var root = candidates.FirstOrDefault(Directory.Exists);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new DirectoryNotFoundException($"Instance artifact directory does not exist: {string.Join(" | ", candidates)}");
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

    private IEnumerable<string> BuildCandidateRoots(InstanceEntity instance)
    {
        var artifactRoot = ResolveRoot();
        var currentVersion = Sanitize(instance.CurrentVersion);
        var instanceType = string.IsNullOrWhiteSpace(instance.InstanceType) ? "department" : instance.InstanceType;
        var instanceId = Sanitize(instance.InstanceId);
        var fromInstanceId = string.IsNullOrWhiteSpace(instance.FromInstanceId)
            ? "unknown"
            : Sanitize(instance.FromInstanceId);

        if (string.Equals(instanceType, "department", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(artifactRoot, "instances", "department", instanceId, "versions", currentVersion);
            yield return Path.Combine(ResolveDigitalWorkforceRoot(), instanceId);
            yield break;
        }

        if (string.Equals(instanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(instanceType, "private_branch", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(artifactRoot, "instances", "personal_clone", fromInstanceId, instanceId, "versions", currentVersion);
            yield return Path.Combine(artifactRoot, "instances", Sanitize(instanceType), fromInstanceId, instanceId, "versions", currentVersion);
            yield return Path.Combine(ResolveDigitalWorkforceRoot(), instanceId);

            if (!string.Equals(fromInstanceId, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(ResolveDigitalWorkforceRoot(), fromInstanceId);
            }

            yield break;
        }

        yield return Path.Combine(artifactRoot, "instances", Sanitize(instanceType), instanceId, "versions", currentVersion);
        yield return Path.Combine(ResolveDigitalWorkforceRoot(), instanceId);
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

    private string ResolveDigitalWorkforceRoot()
    {
        return HireBotPathResolver.ResolveDigitalWorkforceRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:DigitalWorkforceRoot"]);
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
