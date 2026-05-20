using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Migration;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text.Json;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed partial class EmployeeRuntimeService
{
    private static readonly TimeSpan RetirementCleanupTimeout = TimeSpan.FromSeconds(2);

    private int ResolveMaxActivePersonalClonesPerOwner()
    {
        var configured = configuration["HireBot:MaxActivePersonalClonesPerOwner"];
        return int.TryParse(configured, out var value) && value > 0
            ? value
            : DefaultMaxActivePersonalClonesPerOwner;
    }

    /// <summary>
    /// 插入或更新实例记录。
    /// </summary>
    private async Task UpsertInstanceRecordAsync(
        EmployeeDetailDto employee,
        string? currentVersion = null,
        string? describeDocument = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == employee.EmployeeId, cancellationToken);

        var version = string.IsNullOrWhiteSpace(currentVersion)
            ? existing?.CurrentVersion ?? "v_initial"
            : currentVersion.Trim();

        if (existing is null)
        {
            dbContext.Instances.Add(new InstanceEntity
            {
                InstanceId = employee.EmployeeId,
                TenantId = ResolveTenantId(employee),
                InstanceType = string.IsNullOrWhiteSpace(employee.InstanceType) ? "department" : employee.InstanceType,
                Status = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired",
                BasedOnTemplateId = employee.BasedOnTemplateId,
                FromInstanceId = employee.FromInstanceId,
                EvalReportId = null,
                OwnerUserId = string.IsNullOrWhiteSpace(employee.OwnerUserId) ? "unknown" : employee.OwnerUserId,
                DepartmentId = string.IsNullOrWhiteSpace(employee.DepartmentId) ? "department-default" : employee.DepartmentId,
                CurrentVersion = version,
                RuntimeSnapshotJson = JsonSerializer.Serialize(employee),
                DescribeDocument = describeDocument,
                CreatedAt = ParseDate(employee.CreatedAt) ?? now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.TenantId = ResolveTenantId(employee);
            existing.InstanceType = string.IsNullOrWhiteSpace(employee.InstanceType) ? existing.InstanceType : employee.InstanceType;
            existing.Status = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? existing.Status;
            existing.BasedOnTemplateId = employee.BasedOnTemplateId;
            existing.FromInstanceId = employee.FromInstanceId;
            existing.OwnerUserId = string.IsNullOrWhiteSpace(employee.OwnerUserId) ? existing.OwnerUserId : employee.OwnerUserId;
            existing.DepartmentId = string.IsNullOrWhiteSpace(employee.DepartmentId) ? existing.DepartmentId : employee.DepartmentId;
            existing.CurrentVersion = version;
            existing.RuntimeSnapshotJson = JsonSerializer.Serialize(employee);
            if (describeDocument != null)
            {
                existing.DescribeDocument = describeDocument;
            }
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 解析日期。
    /// </summary>
    private static DateTimeOffset? ParseDate(string value)
    {
        if (DateOnly.TryParse(value, out var date))
        {
            return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// 解析租户ID。
    /// </summary>
    private static string ResolveTenantId(EmployeeDetailDto employee)
    {
        if (!string.IsNullOrWhiteSpace(employee.DepartmentId) &&
            !string.Equals(employee.DepartmentId, "department-default", StringComparison.OrdinalIgnoreCase))
        {
            return employee.DepartmentId;
        }

        return string.IsNullOrWhiteSpace(employee.OwningTeam) ? "tenant-default" : employee.OwningTeam;
    }

    /// <summary>
    /// 构建员工ID。
    /// </summary>
    private static string BuildEmployeeId()
    {
        return $"e_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..24];
    }

    /// <summary>
    /// 构建实例ID。
    /// </summary>
    private static string BuildInstanceId(string prefix)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "i" : prefix.Trim().Trim('_');
        return $"{normalizedPrefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..Math.Min(32, normalizedPrefix.Length + 1 + 13 + 1 + 32)];
    }

    /// <summary>
    /// 构建运行时作用域键。
    /// </summary>
    private static string BuildRuntimeScopeKey(string instanceId)
        => $"instance:{instanceId.Trim()}";

    public async Task<ApiResponse<string>> GetRuntimeSandboxGatewayEndpointAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ApiResponse<string>.ErrorResponse(400, "instanceId 不能为空");
        }

        var sandbox = await ResolveRuntimeSandboxAsync(instanceId, cancellationToken);
        if (sandbox is null)
        {
            return ApiResponse<string>.ErrorResponse(404, "runtime sandbox not found");
        }

        if (string.IsNullOrWhiteSpace(sandbox.SandboxId))
        {
            return ApiResponse<string>.ErrorResponse(409, "sandboxId is not ready");
        }

        var refreshResult = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto
            {
                SandboxId = sandbox.SandboxId
            },
            cancellationToken);

        if (!refreshResult.Success || refreshResult.Data is null)
        {
            return ApiResponse<string>.ErrorResponse(refreshResult.Code, refreshResult.Message);
        }

        if (string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint))
        {
            return ApiResponse<string>.ErrorResponse(409, "sandbox gateway endpoint is not ready");
        }

        return ApiResponse<string>.SuccessResponse(refreshResult.Data.GatewayEndpoint.Trim());
    }

    private Task<SandboxInstanceEntity?> ResolveRuntimeSandboxAsync(string instanceId, CancellationToken cancellationToken)
    {
        var normalizedInstanceId = string.IsNullOrWhiteSpace(instanceId) ? null : instanceId.Trim();
        if (normalizedInstanceId is null)
        {
            return Task.FromResult<SandboxInstanceEntity?>(null);
        }

        var scopeKey = BuildRuntimeScopeKey(normalizedInstanceId);
        return dbContext.SandboxInstances
            .AsNoTracking()
            .Where(item =>
                item.ScopeType == SandboxScopeTypes.Hire &&
                item.ScopeKey == scopeKey &&
                item.SandboxRole == RuntimeSandboxRole &&
                item.State != "Deleted")
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 初始化个人分身沙箱。
    /// </summary>
    private async Task<ApiResponse<PersonalCloneSandboxSetupResult>> InitializeRuntimeSandboxAsync(
        EmployeeDetailDto employee,
        string artifactRoot,
        string artifactVersion,
        string ownerSubject,
        string tenantId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var scopeKey = BuildRuntimeScopeKey(employee.EmployeeId);
        var createResponse = await sandboxService.CreateAsync(
            new SandboxCreateRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = scopeKey,
                SandboxRole = RuntimeSandboxRole,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                ProvisioningMode = "managed",
                UseCase = $"runtime-chat-for:{employee.EmployeeId}"
            },
            cancellationToken);
        if (!createResponse.Success || createResponse.Data is null)
        {
            return ApiResponse<PersonalCloneSandboxSetupResult>.ErrorResponse(createResponse.Code, createResponse.Message);
        }

        var readyResponse = await WaitForManagedSandboxReadyAsync(createResponse.Data, cancellationToken);
        if (!readyResponse.Success || readyResponse.Data is null)
        {
            await TryDeleteSandboxAsync(ownerSubject, scopeKey, createResponse.Data.SandboxId, cancellationToken);
            return ApiResponse<PersonalCloneSandboxSetupResult>.ErrorResponse(readyResponse.Code, readyResponse.Message);
        }

        var archiveBytes = BuildArtifactArchiveBytes(artifactRoot);
        var uploadResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
            new DigitalEmployeeTemplateUploadRequestDto
            {
                SandboxId = readyResponse.Data.SandboxId,
                OwnerSubject = ownerSubject,
                ArchiveBytes = archiveBytes,
                FileName = $"{employee.EmployeeId}-{artifactVersion}.zip"
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null || !uploadResult.Data.Success)
        {
            await TryDeleteSandboxAsync(ownerSubject, scopeKey, readyResponse.Data.SandboxId, cancellationToken);
            var message = !string.IsNullOrWhiteSpace(uploadResult.Data?.Error) ? uploadResult.Data.Error : uploadResult.Message;
            return ApiResponse<PersonalCloneSandboxSetupResult>.ErrorResponse(
                uploadResult.Code <= 0 ? 502 : uploadResult.Code,
                string.IsNullOrWhiteSpace(message) ? "个人分身沙箱初始化失败" : message);
        }

        return ApiResponse<PersonalCloneSandboxSetupResult>.SuccessResponse(
            new PersonalCloneSandboxSetupResult(readyResponse.Data.SandboxId, readyResponse.Data.GatewayEndpoint));
    }

    /// <summary>
    /// 创建私有分支前快照当前五件套。私有分支原地更新，因此快照只作为废弃回滚用途。
    /// </summary>
    private async Task SnapshotPrivateBranchArtifactsAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken)
    {
        var resolution = await instanceArtifactResolver.ResolveAsync(instance, cancellationToken);
        var snapshotRoot = BuildPrivateBranchSnapshotRoot(instance);
        ReplaceDirectory(resolution.ArtifactRoot, snapshotRoot, cancellationToken);
    }

    /// <summary>
    /// 废弃私有分支时，从快照恢复原五件套并删除快照。
    /// </summary>
    private async Task RestorePrivateBranchArtifactsAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken)
    {
        var snapshotRoot = BuildPrivateBranchSnapshotRoot(instance);
        if (!Directory.Exists(snapshotRoot))
        {
            throw new DirectoryNotFoundException($"私有分支回滚快照不存在: {snapshotRoot}");
        }

        var resolution = await instanceArtifactResolver.ResolveAsync(instance, cancellationToken);
        ReplaceDirectory(snapshotRoot, resolution.ArtifactRoot, cancellationToken);
        Directory.Delete(snapshotRoot, recursive: true);
    }

    private string BuildPrivateBranchSnapshotRoot(InstanceEntity instance)
    {
        var parentInstanceId = string.IsNullOrWhiteSpace(instance.FromInstanceId)
            ? "unknown"
            : instance.FromInstanceId;
        return Path.Combine(
            ResolveArtifactStoreRoot(),
            "instances",
            "personal_clone",
            SanitizePathSegment(parentInstanceId),
            SanitizePathSegment(instance.InstanceId),
            "snapshots",
            "pre_private_branch");
    }

    private string ResolveArtifactStoreRoot()
    {
        return HireBotPathResolver.ResolveArtifactStoreRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:ArtifactStoreRoot"]);
    }

    private static void ReplaceDirectory(
        string sourceRoot,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"源五件套目录不存在: {sourceRoot}");
        }

        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, recursive: true);
        }

        Directory.CreateDirectory(targetRoot);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var targetPath = Path.Combine(targetRoot, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var trimmed = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }

        return trimmed.Length == 0 ? "unknown" : trimmed;
    }

    /// <summary>
    /// 等待托管沙箱就绪。
    /// </summary>
    private async Task<ApiResponse<SandboxInstanceDto>> WaitForManagedSandboxReadyAsync(
        SandboxInstanceDto instance,
        CancellationToken cancellationToken)
    {
        if (string.Equals(instance.State, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(instance.GatewayEndpoint))
        {
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(instance);
        }

        for (var attempt = 0; attempt < 36; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var refreshResult = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = instance.SandboxId
                },
                cancellationToken);
            if (!refreshResult.Success || refreshResult.Data is null)
            {
                return ApiResponse<SandboxInstanceDto>.ErrorResponse(refreshResult.Code, refreshResult.Message);
            }

            if (string.Equals(refreshResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint))
            {
                return ApiResponse<SandboxInstanceDto>.SuccessResponse(refreshResult.Data);
            }
        }

        return ApiResponse<SandboxInstanceDto>.ErrorResponse(504, "个人分身 sandbox 启动超时");
    }

    /// <summary>
    /// 尝试删除沙箱。
    /// </summary>
    private async Task TryDeleteSandboxAsync(
        string ownerSubject,
        string scopeKey,
        string sandboxId,
        CancellationToken cancellationToken)
    {
        try
        {
            await sandboxService.DeleteAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = sandboxId,
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = scopeKey,
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject
                },
                cancellationToken);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// 退役时清理该实例的运行时资源。
    /// </summary>
    private async Task CleanupRetiredInstanceArtifactsAsync(
        string ownerSubject,
        string instanceId,
        CancellationToken cancellationToken)
    {
        await TryDeleteRuntimeSandboxAsync(ownerSubject, instanceId, cancellationToken);
        await RemoveInstanceImConfigsAsync(instanceId, cancellationToken);
    }

    /// <summary>
    /// 尝试删除分身运行时沙箱。
    /// </summary>
    private async Task TryDeleteRuntimeSandboxAsync(
        string ownerSubject,
        string instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cleanupCts.CancelAfter(RetirementCleanupTimeout);
            await sandboxService.DeleteAsync(
                new SandboxInstanceLookupRequestDto
                {
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = BuildRuntimeScopeKey(instanceId),
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject
                },
                cleanupCts.Token);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// 删除该实例已配置的 IM 绑定。
    /// </summary>
    private async Task RemoveInstanceImConfigsAsync(string instanceId, CancellationToken cancellationToken)
    {
        foreach (var platform in new[] { "feishu", "dingtalk", "wecom" })
        {
            await TryDeleteChannelOverrideAsync(platform, cancellationToken);
        }
    }

    /// <summary>
    /// 删除沙箱内指定频道的运行时覆盖配置。
    /// </summary>
    private async Task TryDeleteChannelOverrideAsync(string platform, CancellationToken cancellationToken)
    {
        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        var path = normalizedPlatform switch
        {
            "feishu" => "/admin/channels/feishu/override",
            "dingtalk" => "/admin/channels/dingtalk/override",
            "wecom" => "/admin/channels/wecom/override",
            _ => null
        };

        if (path is null)
        {
            return;
        }

        try
        {
            var ownerSubject = requestContextService.ResolveOwnerSubject();
            using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cleanupCts.CancelAfter(RetirementCleanupTimeout);
            await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
                HttpMethod.Delete,
                path,
                body: null,
                ownerSubject,
                cleanupCts.Token,
                useHireBotApiPrefix: false);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// KingCrab 管理接口的通用操作结果。
    /// </summary>
    private sealed class KingCrabOperationStatusResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? Mode { get; set; }
    }

    /// <summary>
    /// 构建产物归档字节数组。
    /// </summary>
    private static byte[] BuildArtifactArchiveBytes(string artifactRoot)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var directory in Directory.EnumerateDirectories(artifactRoot, "*", SearchOption.AllDirectories))
            {
                var relativeDirectory = Path.GetRelativePath(artifactRoot, directory)
                    .Replace('\\', '/')
                    .Trim('/');
                if (string.IsNullOrWhiteSpace(relativeDirectory))
                {
                    continue;
                }

                var directoryEntry = archive.CreateEntry($"{relativeDirectory}/");
                directoryEntry.LastWriteTime = File.GetLastWriteTimeUtc(directory);
            }

            foreach (var sourcePath in Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(artifactRoot, sourcePath).Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(sourcePath);
                fileStream.CopyTo(entryStream);
            }
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// 个人分身沙箱设置结果。
    /// </summary>
    private sealed record PersonalCloneSandboxSetupResult(string SandboxId, string? GatewayEndpoint);

    /// <summary>
    /// <summary>
    /// 获取第一个非空值。
    /// </summary>
    private static string Coalesce(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 判断状态流转是否允许。
    /// </summary>
    private static bool IsAllowedTransition(string from, string to)
    {
        if (!AllowedStatusTransitions.TryGetValue(from.Trim(), out var allowed))
        {
            return false;
        }

        return allowed.Contains(to.Trim());
    }

    /// <summary>
    /// 判断是否为可上传技能的实例。
    /// </summary>
    private static bool IsUploadSkillReadyInstance(EmployeeDetailDto employee)
    {
        var status = NormalizeStatus(employee.Status, employee.LifecycleStatus);
        if (!string.Equals(status, "interning_ai", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(employee.EvalPhase))
        {
            return true;
        }

        var phase = employee.EvalPhase.Trim().ToLowerInvariant();
        return phase is "pending_materials" or "pending_skill_upload" or "ai_running";
    }

    /// <summary>
    /// 规范化状态。
    /// </summary>
    private static string? NormalizeStatus(string? status, string? lifecycleStatus)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            return normalized switch
            {
                "hired" => "hired",
                "interning_ai" => "interning_ai",
                "interning_human" => "interning_human",
                "live" => "live",
                "failed" => "failed",
                "retired" => "retired",
                _ => null
            };
        }

        if (!string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            var normalized = lifecycleStatus.Trim().ToLowerInvariant();
            return normalized switch
            {
                "hired" or "待入职" or "已雇佣" => "hired",
                "interning_ai" or "ai评估" or "ai审核" or "待实习" => "interning_ai",
                "interning_human" or "人工评估" or "人工审核" => "interning_human",
                "live" or "上岗" or "在职" => "live",
                "failed" or "失败" => "failed",
                "retired" or "退役" or "离职" => "retired",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// 将状态映射到生命周期标签。
    /// </summary>
    private static string MapStatusToLifecycleLabel(string status)
    {
        return status switch
        {
            "hired" => "已雇佣",
            "interning_ai" => "AI评估中",
            "interning_human" => "人工复核",
            "live" => "已上岗",
            "failed" => "评估失败",
            "retired" => "已退役",
            _ => status
        };
    }
}
