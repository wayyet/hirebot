using HireBot.Abstraction;
using HireBot.Repository.Entities;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// Resolves the artifact root prefix (virtual path) used to package an instance into a runtime sandbox.
/// Uses IFileStore for cross-storage compatibility (local FS / Tencent COS).
/// </summary>
public sealed class InstanceArtifactResolver(
    IFileStore fileStore) : IInstanceArtifactResolver
{
    public async Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = BuildCandidatePrefixes(instance).ToArray();
        foreach (var candidate in candidates)
        {
            if (await PrefixExistsAsync(candidate, cancellationToken))
            {
                return new InstanceArtifactResolution(candidate, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["artifact_root"] = candidate, ["instance_type"] = instance.InstanceType,
                    ["from_instance_id"] = instance.FromInstanceId, ["current_version"] = instance.CurrentVersion
                });
            }
        }
        throw new InvalidOperationException($"Instance artifact not found: {string.Join(" | ", candidates)}");
    }

    private async Task<bool> PrefixExistsAsync(string prefix, CancellationToken ct) =>
        (await fileStore.ListAsync(prefix, ct)).Count > 0;

    private static IEnumerable<string> BuildCandidatePrefixes(InstanceEntity instance)
    {
        var v = Sanitize(instance.CurrentVersion);
        var id = Sanitize(instance.InstanceId);
        var type = string.IsNullOrWhiteSpace(instance.InstanceType) ? "department" : instance.InstanceType;
        var fromId = string.IsNullOrWhiteSpace(instance.FromInstanceId) ? "unknown" : Sanitize(instance.FromInstanceId);

        if (string.Equals(type, "department", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"artifact-store/instances/department/{id}/versions/{v}";
            yield return $"digital-workforce/{id}";
            yield break;
        }
        if (type is "personal_clone" or "private_branch")
        {
            yield return $"artifact-store/instances/personal_clone/{fromId}/{id}/versions/{v}";
            yield return $"personal-clone-artifacts/{fromId}/{id}/versions/{v}";
            yield return $"digital-workforce/{id}";
            if (!string.Equals(fromId, "unknown", StringComparison.OrdinalIgnoreCase))
                yield return $"digital-workforce/{fromId}";
            yield break;
        }
        yield return $"artifact-store/instances/{Sanitize(type)}/{id}/versions/{v}";
        yield return $"digital-workforce/{id}";
    }

    private static string Sanitize(string v) {
        var chars = v.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }
}
