using HireBot.Abstraction;
using HireBot.Core.Services.Internal;
using HireBot.Repository.Entities;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 解析实例的 artifact ZIP 文件路径。
/// 所有 artifact 现在统一为单个 .zip 文件，同时兼容旧的散文件目录。
/// </summary>
public sealed class InstanceArtifactResolver(
    IFileStore fileStore) : IInstanceArtifactResolver
{
    public async Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = BuildCandidateZipPaths(instance).ToArray();
        foreach (var candidate in candidates)
        {
            if (await fileStore.ExistsAsync(candidate, cancellationToken))
            {
                return new InstanceArtifactResolution(candidate, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["artifact_root"] = candidate, ["instance_type"] = instance.InstanceType,
                    ["from_instance_id"] = instance.FromInstanceId, ["current_version"] = instance.CurrentVersion
                });
            }
        }

        // 兼容旧散文件目录
        foreach (var legacy in BuildLegacyCandidatePrefixes(instance))
        {
            if ((await fileStore.ListAsync(legacy, cancellationToken)).Count > 0)
            {
                return new InstanceArtifactResolution(legacy, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["artifact_root"] = legacy, ["instance_type"] = instance.InstanceType,
                    ["from_instance_id"] = instance.FromInstanceId, ["current_version"] = instance.CurrentVersion
                });
            }
        }

        throw new InvalidOperationException($"Instance artifact not found: {string.Join(" | ", candidates)}");
    }

    /// <summary>新的扁平 .zip 路径候选（含租户隔离）</summary>
    private static IEnumerable<string> BuildCandidateZipPaths(InstanceEntity instance)
    {
        var v = Sanitize(instance.CurrentVersion);
        var id = Sanitize(instance.InstanceId);
        var type = string.IsNullOrWhiteSpace(instance.InstanceType) ? "department" : instance.InstanceType;
        var fromId = string.IsNullOrWhiteSpace(instance.FromInstanceId) ? "unknown" : Sanitize(instance.FromInstanceId);
        var tenantId = string.IsNullOrWhiteSpace(instance.TenantId) ? "default" : Sanitize(instance.TenantId);

        if (string.Equals(type, "department", StringComparison.OrdinalIgnoreCase))
        {
            yield return ArtifactStoragePaths.BuildDepartmentVersionPath(tenantId, id, v);
            yield return ArtifactStoragePaths.BuildDigitalWorkforcePath(tenantId, id);
            yield break;
        }
        if (type is "personal_clone" or "private_branch")
        {
            yield return ArtifactStoragePaths.BuildPersonalCloneVersionPath(tenantId, fromId, id, v);
            yield return ArtifactStoragePaths.BuildPersonalCloneArtifactsPath(tenantId, fromId, id, v);
            yield return ArtifactStoragePaths.BuildDigitalWorkforcePath(tenantId, id);
            if (!string.Equals(fromId, "unknown", StringComparison.OrdinalIgnoreCase))
                yield return ArtifactStoragePaths.BuildDigitalWorkforcePath(tenantId, fromId);
            yield break;
        }
        yield return ArtifactStoragePaths.BuildDepartmentVersionPath(tenantId, id, v);
        yield return ArtifactStoragePaths.BuildDigitalWorkforcePath(tenantId, id);
    }

    /// <summary>旧散文件目录前缀（兼容）</summary>
    private static IEnumerable<string> BuildLegacyCandidatePrefixes(InstanceEntity instance)
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

    private static string Sanitize(string v) => ArtifactStoragePaths.Sanitize(v);
}
