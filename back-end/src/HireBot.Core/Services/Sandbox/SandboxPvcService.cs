using k8s;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenSandbox.Models;

namespace HireBot.Core.Services.Sandbox;

internal sealed class SandboxPvcService
{
    private readonly IKubernetes? kubernetes;
    private readonly IConfiguration configuration;
    private readonly ILogger<SandboxPvcService> logger;

    public SandboxPvcService(IConfiguration configuration, ILogger<SandboxPvcService> logger)
    {
        this.configuration = configuration;
        this.logger = logger;

        if (!configuration.GetValue<bool>("SandboxPvc:Enabled"))
        {
            kubernetes = null;
            return;
        }

        var kubeConfigPath = configuration["SandboxPvc:KubeConfigPath"];
        KubernetesClientConfiguration kubeConfig;
        if (!string.IsNullOrWhiteSpace(kubeConfigPath) && File.Exists(kubeConfigPath))
        {
            kubeConfig = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeConfigPath);
        }
        else
        {
            try
            {
                kubeConfig = KubernetesClientConfiguration.InClusterConfig();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "无法加载 Kubernetes 配置，Sandbox PVC 持久卷能力将被跳过");
                kubernetes = null;
                return;
            }
        }

        kubernetes = new Kubernetes(kubeConfig);
    }

    // PVC 按 scopeKey（hireId / instanceId 等会话唯一键）命名，确保每次会话隔离，
    // 不同模板或重建后的新沙箱不会意外挂载旧数据。
    internal string WorkspacePvcName(string scopeKey) => $"kc-ws-{SanitizeForPvc(scopeKey)}";

    internal string MemoryPvcName(string scopeKey) => $"kc-mem-{SanitizeForPvc(scopeKey)}";

    internal IReadOnlyList<Volume> BuildVolumes(string scopeKey)
    {
        var workspaceName = WorkspacePvcName(scopeKey);
        var memoryName = MemoryPvcName(scopeKey);

        return
        [
            new Volume
            {
                Name = "kc-workspace",
                Pvc = new PVC { ClaimName = workspaceName },
                MountPath = "/workspace"
            },
            new Volume
            {
                Name = "kc-memory",
                Pvc = new PVC { ClaimName = memoryName },
                MountPath = "/app/memory"
            }
        ];
    }

    /// <summary>
    /// 确保会话 PVC 存在（按 scopeKey 隔离，每次新建沙箱时调用）。
    /// </summary>
    public async Task<IReadOnlyList<Volume>> EnsureSessionPvcsAsync(
        string ownerSubject, string scopeKey, CancellationToken cancellationToken = default)
    {
        if (kubernetes is null)
        {
            return [];
        }

        var @namespace = configuration["SandboxPvc:Namespace"] ?? "opensandbox";
        var accessMode = configuration["SandboxPvc:AccessMode"] ?? "ReadWriteOnce";
        var workspaceSize = configuration["SandboxPvc:WorkspaceSize"] ?? "10Gi";
        var memorySize = configuration["SandboxPvc:MemorySize"] ?? "2Gi";

        await EnsurePvcAsync(@namespace, WorkspacePvcName(scopeKey), workspaceSize, accessMode, "workspace", ownerSubject, cancellationToken);
        await EnsurePvcAsync(@namespace, MemoryPvcName(scopeKey), memorySize, accessMode, "memory", ownerSubject, cancellationToken);

        return BuildVolumes(scopeKey);
    }

    /// <summary>
    /// 删除会话 PVC（沙箱删除时调用，避免旧数据被新会话挂载）。
    /// </summary>
    public async Task DeletePvcsAsync(string scopeKey, CancellationToken cancellationToken = default)
    {
        if (kubernetes is null)
        {
            return;
        }

        var @namespace = configuration["SandboxPvc:Namespace"] ?? "opensandbox";
        await TryDeletePvcAsync(@namespace, WorkspacePvcName(scopeKey), cancellationToken);
        await TryDeletePvcAsync(@namespace, MemoryPvcName(scopeKey), cancellationToken);
    }

    private async Task TryDeletePvcAsync(string @namespace, string pvcName, CancellationToken cancellationToken)
    {
        try
        {
            await kubernetes!.CoreV1.DeleteNamespacedPersistentVolumeClaimAsync(pvcName, @namespace, cancellationToken: cancellationToken);
            logger.LogInformation("PVC {PvcName} 已删除", pvcName);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogDebug("PVC {PvcName} 不存在，跳过删除", pvcName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "删除 PVC {PvcName} 失败（不阻断流程）", pvcName);
        }
    }

    private async Task EnsurePvcAsync(
        string @namespace,
        string pvcName,
        string size,
        string accessMode,
        string role,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var storageClassName = configuration["SandboxPvc:StorageClassName"];
        var sanitizedOwner = SanitizeForPvc(ownerSubject);

        var pvc = new V1PersistentVolumeClaim
        {
            ApiVersion = "v1",
            Kind = "PersistentVolumeClaim",
            Metadata = new V1ObjectMeta
            {
                Name = pvcName,
                NamespaceProperty = @namespace,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "hirebot",
                    ["managed-by"] = "hirebot",
                    ["role"] = role,
                    ["owner-subject"] = sanitizedOwner[..Math.Min(sanitizedOwner.Length, 63)]
                }
            },
            Spec = new V1PersistentVolumeClaimSpec
            {
                AccessModes = [accessMode],
                Resources = new V1VolumeResourceRequirements
                {
                    Requests = new Dictionary<string, ResourceQuantity>
                    {
                        ["storage"] = new ResourceQuantity(size)
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(storageClassName))
        {
            pvc.Spec.StorageClassName = storageClassName;
        }

        try
        {
            await kubernetes!.CoreV1.CreateNamespacedPersistentVolumeClaimAsync(pvc, @namespace, cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            logger.LogDebug("PVC {PvcName} 已存在，跳过创建", pvcName);
        }
    }

    // PVC 名称最多 63 字符；前缀 "kc-ws-" 占 6 位，scopeKey 最多 50 位。
    private static string SanitizeForPvc(string scopeKey)
    {
        var sanitized = scopeKey
            .ToLowerInvariant()
            .Replace(':', '-')
            .Replace('_', '-')
            .Replace('.', '-');
        return sanitized.Length <= 50 ? sanitized : sanitized[..50];
    }
}
