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

    internal string WorkspacePvcName(string ownerSubject) => $"kc-ws-{SanitizeOwner(ownerSubject)}";

    internal string MemoryPvcName(string ownerSubject) => $"kc-mem-{SanitizeOwner(ownerSubject)}";

    internal IReadOnlyList<Volume> BuildVolumes(string ownerSubject)
    {
        var workspaceName = WorkspacePvcName(ownerSubject);
        var memoryName = MemoryPvcName(ownerSubject);

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

    public async Task<IReadOnlyList<Volume>> EnsureUserPvcsAsync(string ownerSubject, CancellationToken cancellationToken = default)
    {
        if (kubernetes is null)
        {
            return [];
        }

        var @namespace = configuration["SandboxPvc:Namespace"] ?? "opensandbox";
        var accessMode = configuration["SandboxPvc:AccessMode"] ?? "ReadWriteOnce";
        var workspaceSize = configuration["SandboxPvc:WorkspaceSize"] ?? "10Gi";
        var memorySize = configuration["SandboxPvc:MemorySize"] ?? "2Gi";

        await EnsurePvcAsync(@namespace, WorkspacePvcName(ownerSubject), workspaceSize, accessMode, "workspace", ownerSubject, cancellationToken);
        await EnsurePvcAsync(@namespace, MemoryPvcName(ownerSubject), memorySize, accessMode, "memory", ownerSubject, cancellationToken);

        return BuildVolumes(ownerSubject);
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
        var sanitizedOwner = SanitizeOwner(ownerSubject);

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

    private static string SanitizeOwner(string ownerSubject)
    {
        return ownerSubject
            .ToLowerInvariant()
            .Replace(':', '-')
            .Replace('_', '-');
    }
}
