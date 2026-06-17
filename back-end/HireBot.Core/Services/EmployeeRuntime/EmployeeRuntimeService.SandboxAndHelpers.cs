using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed partial class EmployeeRuntimeService
{
    private static readonly TimeSpan RetirementSandboxCleanupTimeout = TimeSpan.FromSeconds(30);

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
        string? description = null,
        string? describeDocument = null,
        string? hireId = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == employee.EmployeeId, cancellationToken);

        var version = string.IsNullOrWhiteSpace(currentVersion)
            ? existing?.CurrentVersion ?? "v_initial"
            : currentVersion.Trim();

        // 如果未提供 description，尝试从 employee.Description 获取（可能在构建DTO时已设置）
        var finalDescription = description ?? employee.Description;

        // 如果未提供 describeDocument，尝试从源 Instance 复制
        if (string.IsNullOrWhiteSpace(describeDocument) && existing is null)
        {
            describeDocument = await TryInheritDescribeDocumentAsync(
                employee.FromInstanceId,
                employee.BasedOnTemplateId,
                cancellationToken);
        }

        if (existing is null)
        {
            var tenantId = GetCurrentTenantId();
            dbContext.Instances.Add(new InstanceEntity
            {
                InstanceId = employee.EmployeeId,
                TenantId = tenantId,
                InstanceType = string.IsNullOrWhiteSpace(employee.InstanceType) ? "department" : employee.InstanceType,
                Status = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired",
                BasedOnTemplateId = employee.BasedOnTemplateId,
                HireId = string.IsNullOrWhiteSpace(hireId) ? null : hireId.Trim(),
                FromInstanceId = employee.FromInstanceId,
                EvalReportId = null,
                OwnerUserId = string.IsNullOrWhiteSpace(employee.OwnerUserId) ? "unknown" : employee.OwnerUserId,
                DepartmentId = tenantId,
                CurrentVersion = version,
                RuntimeSnapshotJson = JsonSerializer.Serialize(employee),
                Description = finalDescription,
                DescribeDocument = describeDocument,
                CreatedAt = employee.CreatedAt,
                UpdatedAt = now
            });
        }
        else
        {
            var tenantId = GetCurrentTenantId();
            existing.TenantId = tenantId;
            existing.InstanceType = string.IsNullOrWhiteSpace(employee.InstanceType) ? existing.InstanceType : employee.InstanceType;
            existing.Status = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? existing.Status;
            existing.BasedOnTemplateId = employee.BasedOnTemplateId;
            existing.FromInstanceId = employee.FromInstanceId;
            // 若首次写入时 HireId 为空而本次提供了值，则补齐（仅允许从无到有，不允许覆盖）
            if (string.IsNullOrWhiteSpace(existing.HireId) && !string.IsNullOrWhiteSpace(hireId))
            {
                existing.HireId = hireId.Trim();
            }
            existing.OwnerUserId = string.IsNullOrWhiteSpace(employee.OwnerUserId) ? existing.OwnerUserId : employee.OwnerUserId;
            existing.DepartmentId = tenantId;
            existing.CurrentVersion = version;
            existing.RuntimeSnapshotJson = JsonSerializer.Serialize(employee);
            if (!string.IsNullOrWhiteSpace(finalDescription))
            {
                existing.Description = finalDescription;
            }
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
        // 优先解析带时间的格式，保留完整精度
        if (DateTime.TryParse(value, out var dt))
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        if (DateOnly.TryParse(value, out var date))
        {
            return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        return null;
    }

    /// <summary>
    /// 尝试从源 Instance 或模板 Instance 继承 DescribeDocument。
    /// </summary>
    private async Task<string?> TryInheritDescribeDocumentAsync(
        string? fromInstanceId,
        string? basedOnTemplateId,
        CancellationToken cancellationToken)
    {
        // 优先从 FromInstanceId（直接克隆源）获取
        if (!string.IsNullOrWhiteSpace(fromInstanceId))
        {
            var fromInstance = await dbContext.Instances
                .AsNoTracking()
                .Where(i => i.InstanceId == fromInstanceId)
                .Select(i => i.DescribeDocument)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(fromInstance))
            {
                return fromInstance;
            }
        }

        // 其次从 BasedOnTemplateId 查找模板 Instance
        // 假设模板ID对应的Instance记录中保存了模板的 describe.md
        if (!string.IsNullOrWhiteSpace(basedOnTemplateId))
        {
            // 尝试查找以模板ID为InstanceId的记录（模板自身的Instance）
            var templateInstance = await dbContext.Instances
                .AsNoTracking()
                .Where(i => i.InstanceId == basedOnTemplateId || i.BasedOnTemplateId == basedOnTemplateId)
                .Select(i => i.DescribeDocument)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(templateInstance))
            {
                return templateInstance;
            }
        }

        return null;
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
            // DB 中无记录（沙箱从未创建或被标记删除），尝试自动重建。
            // PVC 按 scopeKey 持久保留，重建容器后工作区数据自动恢复，无需重新上传技能包。
            var instance = await dbContext.Instances
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.InstanceId == instanceId, cancellationToken);
            if (instance is null)
            {
                return ApiResponse<string>.ErrorResponse(404, "instance not found");
            }

            var tenantId = instance.TenantId ?? GetCurrentTenantId() ?? "default";
            var operatorId = instance.OwnerUserId ?? userIdentity.OperatorId;
            var ownerSubject = userIdentity.OwnerSubject;
            var scopeKey = BuildRuntimeScopeKey(instanceId);

            // runtime sandbox 记录缺失，自动重建（PVC 持久保留，容器重建后工作区数据自动恢复）
            var createResponse = await sandboxService.CreateAsync(
                new SandboxCreateRequestDto
                {
                    ScopeType = SandboxScopeTypes.Runtime,
                    ScopeKey = scopeKey,
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject,
                    TenantId = tenantId,
                    OperatorId = operatorId,
                    ProvisioningMode = "managed",
                    UseCase = $"runtime-chat-for:{instanceId}",
                    Metadata = BuildRuntimeSandboxMeta(ownerSubject, instanceId, instance.BasedOnTemplateId, tenantId)
                },
                cancellationToken);
            if (!createResponse.Success || createResponse.Data is null)
            {
                return ApiResponse<string>.ErrorResponse(createResponse.Code, createResponse.Message);
            }

            var readyResponse = await WaitForManagedSandboxReadyAsync(createResponse.Data, cancellationToken);
            if (!readyResponse.Success || readyResponse.Data is null)
            {
                return ApiResponse<string>.ErrorResponse(readyResponse.Code, readyResponse.Message);
            }

            return string.IsNullOrWhiteSpace(readyResponse.Data.GatewayEndpoint)
                ? ApiResponse<string>.ErrorResponse(409, "sandbox gateway endpoint is not ready")
                : ApiResponse<string>.SuccessResponse(readyResponse.Data.GatewayEndpoint.Trim());
        }

        if (string.IsNullOrWhiteSpace(sandbox.SandboxId))
        {
            return ApiResponse<string>.ErrorResponse(409, "sandboxId is not ready");
        }

        // 传入完整 scope 信息：
        // 若 SandboxId 因并发重建而过期，RefreshAsync 可回退到 scope 查询，避免 "OwnerSubject is required" 400
        var refreshResult = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto
            {
                SandboxId = sandbox.SandboxId,
                ScopeType = sandbox.ScopeType,
                ScopeKey = sandbox.ScopeKey,
                SandboxRole = sandbox.SandboxRole,
                OwnerSubject = sandbox.OwnerSubject,
                TenantId = sandbox.TenantId,
                OperatorId = sandbox.OperatorId
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
                item.ScopeType == SandboxScopeTypes.Runtime &&
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
                ScopeType = SandboxScopeTypes.Runtime,
                ScopeKey = scopeKey,
                SandboxRole = RuntimeSandboxRole,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                ProvisioningMode = "managed",
                UseCase = $"runtime-chat-for:{employee.EmployeeId}",
                Metadata = BuildRuntimeSandboxMeta(ownerSubject, employee.EmployeeId, employee.BasedOnTemplateId ?? employee.SourceTemplateId, tenantId)
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

        var archiveBytes = await BuildArtifactArchiveBytesAsync(artifactRoot, cancellationToken);
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
        var snapshotPrefix = BuildPrivateBranchSnapshotPrefix(instance);
        await ReplaceDirectoryAsync(resolution.ArtifactRoot, snapshotPrefix, cancellationToken);
    }

    /// <summary>
    /// 废弃私有分支时，从快照恢复原五件套并删除快照。
    /// </summary>
    private async Task RestorePrivateBranchArtifactsAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken)
    {
        var snapshotPrefix = BuildPrivateBranchSnapshotPrefix(instance);
        if (!await PrefixHasFilesAsync(snapshotPrefix, cancellationToken))
            throw new InvalidOperationException($"私有分支回滚快照不存在: {snapshotPrefix}");

        var resolution = await instanceArtifactResolver.ResolveAsync(instance, cancellationToken);
        await ReplaceDirectoryAsync(snapshotPrefix, resolution.ArtifactRoot, cancellationToken);

        var entries = await fileStore.ListAsync(snapshotPrefix, cancellationToken);
        foreach (var e in entries)
            await fileStore.DeleteAsync(e.Path, cancellationToken);
    }

    private static string BuildPrivateBranchSnapshotPrefix(InstanceEntity instance)
    {
        var parentId = string.IsNullOrWhiteSpace(instance.FromInstanceId) ? "unknown"
            : ArtifactStoragePaths.Sanitize(instance.FromInstanceId);
        var tenantId = string.IsNullOrWhiteSpace(instance.TenantId) ? "default" : instance.TenantId;
        return ArtifactStoragePaths.BuildSnapshotPath(tenantId, parentId, instance.InstanceId);
    }

    private async Task ReplaceDirectoryAsync(string srcPath, string dstPath, CancellationToken ct)
    {
        // 新路径: 都是 .zip 文件，直接复制
        if (srcPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (!await fileStore.ExistsAsync(srcPath, ct))
                throw new InvalidOperationException($"源产物 ZIP 不存在: {srcPath}");

            // 删除旧目标
            if (await fileStore.ExistsAsync(dstPath, ct))
                await fileStore.DeleteAsync(dstPath, ct);

            // 复制 ZIP
            await using var s = await fileStore.OpenReadAsync(srcPath, ct);
            await fileStore.SaveAsync(dstPath, s, ct);
            return;
        }

        // 兼容旧散文件目录
        if (!await PrefixHasFilesAsync(srcPath, ct))
            throw new InvalidOperationException($"源产物不存在: {srcPath}");

        var dstEntries = await fileStore.ListAsync(dstPath, ct);
        foreach (var e in dstEntries)
            await fileStore.DeleteAsync(e.Path, ct);

        var srcEntries = await fileStore.ListAsync(srcPath, ct);
        foreach (var entry in srcEntries)
        {
            ct.ThrowIfCancellationRequested();
            var rel = entry.Path;
            if (rel.StartsWith(srcPath, StringComparison.OrdinalIgnoreCase))
                rel = rel[srcPath.Length..].TrimStart('/');
            if (string.IsNullOrWhiteSpace(rel)) continue;

            await using var s = await fileStore.OpenReadAsync(entry.Path, ct);
            await fileStore.SaveAsync($"{dstPath}/{rel}", s, ct);
        }
    }

    private async Task<bool> PrefixHasFilesAsync(string prefix, CancellationToken ct) =>
        (await fileStore.ListAsync(prefix, ct)).Count > 0;

    private static string SanitizePathSegment(string value)
    {
        var trimmed = value.Trim();
        var chars = trimmed.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }

    /// <summary>
    /// 删除新 .zip 格式和旧散文件目录的 artifact 文件（尽力清理）。
    /// </summary>
    private async Task TryDeleteZipAndLegacyAsync(string zipPath, string legacyPrefix, CancellationToken ct)
    {
        // 删除新 .zip 文件
        try
        {
            if (await fileStore.ExistsAsync(zipPath, ct))
                await fileStore.DeleteAsync(zipPath, ct);
        }
        catch { /* 尽力清理 */ }

        // 删除旧散文件目录
        try
        {
            var entries = await fileStore.ListAsync(legacyPrefix, ct);
            foreach (var entry in entries)
                await fileStore.DeleteAsync(entry.Path, ct);
        }
        catch { /* 尽力清理 */ }
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
                    ScopeType = SandboxScopeTypes.Runtime,
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
    /// IM 渠道覆盖配置存储在 KingCrab 网关层（按 ownerSubject 全局生效），由沙箱容器自身管理，
    /// 沙箱删除后 KingCrab 路由失败会自愈，无需 HireBot 主动清理（否则可能误伤同 owner 下其他在线员工）。
    /// </summary>
    private Task CleanupRetiredInstanceArtifactsAsync(
        string ownerSubject,
        string instanceId,
        CancellationToken cancellationToken)
    {
        return TryDeleteRuntimeSandboxAsync(ownerSubject, instanceId, cancellationToken);
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
            cleanupCts.CancelAfter(RetirementSandboxCleanupTimeout);
            await sandboxService.DeleteAsync(
                new SandboxInstanceLookupRequestDto
                {
                    ScopeType = SandboxScopeTypes.Runtime,
                    ScopeKey = BuildRuntimeScopeKey(instanceId),
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject
                },
                cleanupCts.Token);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup only.
            logger.LogWarning(ex, "退役时删除运行时沙箱失败（instanceId={InstanceId}, ownerSubject={OwnerSubject}），已忽略",
                instanceId, ownerSubject);
        }
    }

    /// <summary>
    /// 构建产物归档字节数组。如果是 .zip 路径则直接读取透传，
    /// 兼容旧散文件目录（ListAsync + N×OpenReadAsync）。
    /// </summary>
    private async Task<byte[]> BuildArtifactArchiveBytesAsync(string artifactPrefix, CancellationToken cancellationToken)
    {
        // 新路径: 直接是 .zip 文件，一次性读取透传
        if (artifactPrefix.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && await fileStore.ExistsAsync(artifactPrefix, cancellationToken))
        {
            await using var zipStream = await fileStore.OpenReadAsync(artifactPrefix, cancellationToken);
            using var ms = new MemoryStream();
            await zipStream.CopyToAsync(ms, cancellationToken);
            return ms.ToArray();
        }

        // 兼容旧散文件目录
        var entries = await fileStore.ListAsync(artifactPrefix, cancellationToken);
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = entry.Path;
                if (relative.StartsWith(artifactPrefix, StringComparison.OrdinalIgnoreCase))
                    relative = relative[artifactPrefix.Length..].TrimStart('/');
                if (string.IsNullOrWhiteSpace(relative)) continue;

                var lastSlash = relative.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    var dir = relative[..lastSlash];
                    if (directories.Add(dir))
                    {
                        var dirEntry = archive.CreateEntry($"{dir}/");
                        dirEntry.LastWriteTime = DateTimeOffset.UtcNow;
                    }
                }

                var fileEntry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                await using var entryStream = fileEntry.Open();
                await using var sourceStream = await fileStore.OpenReadAsync(entry.Path, cancellationToken);
                await sourceStream.CopyToAsync(entryStream, cancellationToken);
            }
        }
        return memoryStream.ToArray();
    }

    /// <summary>
    /// 个人分身沙箱设置结果。
    /// </summary>
    private sealed record PersonalCloneSandboxSetupResult(string SandboxId, string? GatewayEndpoint);

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
                "hiring" => "hiring",
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
                "hiring" or "雇佣中" => "hiring",
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
            "hiring" => "雇佣中",
            "hired" => "已雇佣",
            "interning_ai" => "AI评估中",
            "interning_human" => "人工复核",
            "live" => "已上岗",
            "failed" => "评估失败",
            "retired" => "已退役",
            _ => status
        };
    }

    /// <summary>
    /// 构建个人运行时沙箱元数据。
    /// </summary>
    private static Dictionary<string, string> BuildRuntimeSandboxMeta(
        string ownerSubject, string instanceId, string? templateId, string tenantId = "")
    {
        var meta = new Dictionary<string, string>
        {
            [SandboxMetaKeys.UserSubject] = ownerSubject,
            [SandboxMetaKeys.InstanceId] = instanceId
        };
        if (!string.IsNullOrWhiteSpace(templateId))
            meta[SandboxMetaKeys.TemplateId] = templateId;
        if (!string.IsNullOrWhiteSpace(tenantId))
            meta[SandboxMetaKeys.TenantId] = tenantId;
        return meta;
    }

    // JSON 选项：用于反序列化 HiringExternalSystemConfigState
    private static readonly JsonSerializerOptions McpSyncJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 将源员工雇佣流程中保存的外部系统 MCP 配置与克隆沙箱已有配置合并后写回沙箱。
    /// 合并策略：克隆沙箱现有配置为基础，数据库用户自定义 MCP 服务按 Name 覆盖（优先级更高）。
    /// 非致命：失败仅记录警告，不中断分身创建流程。
    /// </summary>
    private async Task SyncMcpConfigToCloneSandboxAsync(
        string sourceEmployeeId,
        string cloneSandboxId,
        string? gatewayEndpoint,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. 验证克隆沙箱 Gateway 端点
            if (string.IsNullOrWhiteSpace(gatewayEndpoint))
            {
                logger.LogWarning(
                    "SyncMcpConfigToCloneSandboxAsync: 克隆沙箱 GatewayEndpoint 为空，跳过 MCP 配置同步: CloneSandboxId={CloneSandboxId}",
                    cloneSandboxId);
                return;
            }

            // 2. 从源员工实例获取关联的 HireId
            var sourceInstance = await dbContext.Instances
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.InstanceId == sourceEmployeeId, cancellationToken);

            if (string.IsNullOrWhiteSpace(sourceInstance?.HireId))
            {
                logger.LogDebug(
                    "SyncMcpConfigToCloneSandboxAsync: 源员工 {SourceEmployeeId} 无关联 HireId，跳过 MCP 配置同步",
                    sourceEmployeeId);
                return;
            }

            var hireId = sourceInstance.HireId;

            // 3. 从数据库读取用户配置的 MCP 服务（HiringExternalConfigs 表）
            IReadOnlyList<HiringMcpServerConfigDto> userMcpServers = [];
            var externalConfigEntity = await dbContext.HiringExternalConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

            if (externalConfigEntity is not null && externalConfigEntity.ConfigJson != "{}")
            {
                try
                {
                    var state = JsonSerializer.Deserialize<HiringExternalSystemConfigState>(
                        externalConfigEntity.ConfigJson, McpSyncJsonOptions);
                    var dto = state?.ToDto(secretProtector);
                    if (dto?.McpServers is { Count: > 0 })
                    {
                        userMcpServers = dto.McpServers;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "SyncMcpConfigToCloneSandboxAsync: 反序列化外部系统 MCP 配置失败，跳过用户自定义配置: HireId={HireId}",
                        hireId);
                }
            }

            // 4. 读取克隆沙箱中已有的 MCP 配置（克隆时从源沙箱继承的配置）
            // GET /admin/workspace/mcp 返回 { builtin: {...}, user: {...} }，
            // PUT /admin/workspace/mcp 只接受 user 层的 SandboxWorkspaceMcpConfig，
            // 所以读时从 user 字段提取，写时直接提交合并后的 SandboxWorkspaceMcpConfig。
            var mergedServers = new Dictionary<string, SandboxMcpServerEntry>(StringComparer.OrdinalIgnoreCase);
            var getResult = await kingCrabHttpClient.SendForJsonAsync<SandboxWorkspaceMcpGetResponse>(
                HttpMethod.Get,
                "/admin/workspace/mcp",
                null,
                ownerSubject,
                cancellationToken,
                useHireBotApiPrefix: false,
                absoluteBaseUrl: gatewayEndpoint);

            if (getResult.Success && getResult.Data?.User?.Servers is { Count: > 0 })
            {
                foreach (var (key, entry) in getResult.Data.User.Servers)
                {
                    mergedServers[key] = entry;
                }
            }

            // 5. 将用户 DB 配置的 MCP 服务器合并（DB 配置按 Name 覆盖沙箱已有同名条目）
            // HiringMcpServerConfigDto 有三种 token 传递方式，全部展开为 Headers：
            //   a. Headers           — 直接使用，原样保留
            //   b. BearerTokenEnv    — 从 Env[BearerTokenEnv] 取值，组装为 Authorization: Bearer {token}
            //   c. HeadersFromEnv    — {headerName: envKey} 映射，从 Env[envKey] 取值补入 Headers
            foreach (var mcp in userMcpServers)
            {
                if (string.IsNullOrWhiteSpace(mcp.Name) || string.IsNullOrWhiteSpace(mcp.Url))
                {
                    continue;
                }

                // 展开所有 token/header 来源为最终 Headers 字典
                var resolvedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // a. 直接 Headers
                foreach (var (k, v) in mcp.Headers)
                {
                    if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                        resolvedHeaders[k] = v;
                }

                // b. BearerTokenEnv — 前端填入的是 token 本身（不是环境变量名），
                //    直接组装为 Authorization: Bearer {token}。
                //    注意：后端 FromDto 存储时 ProtectedEnv 永远为空，
                //    因此无法通过 Env 字典查找，BearerTokenEnv 字段值即为明文 token。
                if (!string.IsNullOrWhiteSpace(mcp.BearerTokenEnv))
                {
                    resolvedHeaders["Authorization"] = $"Bearer {mcp.BearerTokenEnv}";
                }

                // c. HeadersFromEnv → {headerName: Env[envKey]}
                foreach (var (headerName, envKey) in mcp.HeadersFromEnv)
                {
                    if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(envKey))
                        continue;
                    if (mcp.Env.TryGetValue(envKey, out var envVal) && !string.IsNullOrWhiteSpace(envVal))
                        resolvedHeaders[headerName] = envVal;
                }

                mergedServers[mcp.Name] = new SandboxMcpServerEntry
                {
                    Transport = mcp.Transport,
                    Url = mcp.Url,
                    Enabled = true,
                    Name = mcp.Name,
                    Headers = resolvedHeaders.Count > 0 ? resolvedHeaders : null,
                };
            }

            if (mergedServers.Count == 0)
            {
                logger.LogDebug(
                    "SyncMcpConfigToCloneSandboxAsync: 合并后无有效 MCP 服务器配置，跳过上传: CloneSandboxId={CloneSandboxId}",
                    cloneSandboxId);
                return;
            }

            // 6. 上传合并后的 MCP 配置到克隆沙箱
            var mergedConfig = new SandboxWorkspaceMcpConfig { Enabled = true, Servers = mergedServers };
            var uploadResult = await kingCrabHttpClient.SendForJsonAsync<JsonElement>(
                HttpMethod.Put,
                "/admin/workspace/mcp",
                mergedConfig,
                ownerSubject,
                cancellationToken,
                useHireBotApiPrefix: false,
                absoluteBaseUrl: gatewayEndpoint);

            if (!uploadResult.Success)
            {
                logger.LogWarning(
                    "SyncMcpConfigToCloneSandboxAsync: MCP 配置上传失败（非致命）: CloneSandboxId={CloneSandboxId}, Message={Message}",
                    cloneSandboxId,
                    uploadResult.Message);
                return;
            }

            logger.LogInformation(
                "SyncMcpConfigToCloneSandboxAsync: 已将合并后的 MCP 配置同步到克隆沙箱: CloneSandboxId={CloneSandboxId}, ServerCount={ServerCount}",
                cloneSandboxId,
                mergedServers.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "SyncMcpConfigToCloneSandboxAsync: 同步 MCP 配置异常（非致命）: CloneSandboxId={CloneSandboxId}",
                cloneSandboxId);
        }
    }

    /// <summary>
    /// GET /admin/workspace/mcp 返回的外层包装结构：
    /// { builtin: { Enabled, Servers: {...} }, user: { Enabled, Servers: {...} } }
    /// PUT /admin/workspace/mcp 只写 user 部分，所以读时只取 user。
    /// </summary>
    private sealed class SandboxWorkspaceMcpGetResponse
    {
        [JsonPropertyName("user")]
        public SandboxWorkspaceMcpConfig? User { get; init; }
    }
}
