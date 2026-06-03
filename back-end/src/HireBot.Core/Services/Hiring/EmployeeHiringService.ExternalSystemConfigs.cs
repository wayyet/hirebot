using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    public async Task<ApiResponse<HiringExternalSystemConfigDto>> GetExternalSystemConfigAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken)
                             ?? hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(404, "未找到该雇佣流程的运行时状态");
        }

        runtimeContext = await EnsureExternalSystemConfigHydratedAsync(runtimeContext, cancellationToken);
        return ApiResponse<HiringExternalSystemConfigDto>.SuccessResponse(
            runtimeContext.ExternalSystemConfig?.ToDto(secretProtector) ?? new HiringExternalSystemConfigDto());
    }

    public async Task<ApiResponse<HiringExternalSystemConfigDto>> SaveExternalSystemConfigAsync(
        string hireId,
        HiringExternalSystemConfigDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken)
                             ?? hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(404, "未找到该雇佣流程的运行时状态");
        }

        runtimeContext = await EnsureExternalSystemConfigHydratedAsync(runtimeContext, cancellationToken);

        HiringExternalSystemConfigState normalizedState;
        try
        {
            normalizedState = HiringExternalSystemConfigState.FromDto(request, secretProtector);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to protect external system config. HireId={HireId}", normalizedHireId);
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(500, "外部系统敏感配置加密失败，请稍后重试");
        }

        var persistedState = normalizedState.IsPersisted ? normalizedState : null;

        runtimeContext = runtimeContext with
        {
            ExternalSystemConfig = persistedState
        };
        runtimeContext = ApplyConversationProgressToTemplatePackage(runtimeContext);

        if (persistedState is not null)
        {
            if (IsCollectionStageBeforeReadyForPackaging(runtimeContext.CurrentStage))
            {
                runtimeContext = runtimeContext with
                {
                    CurrentStage = HiringCollectionStage.ReadyForPackaging
                };
            }

            runtimeContext = MarkPackagingTestCasesWaitingConfirmIfNeeded(runtimeContext);
        }

        hiringRuntimeStore.Upsert(runtimeContext);

        await UpsertExternalSystemConfigMetadataAsync(runtimeContext.SandboxId, persistedState, cancellationToken);
        await SyncExternalSystemConfigWorkspaceAsync(runtimeContext, cancellationToken);

        // 外部配置变更后立即持久化中间包，确保后端存储的包始终包含最新外部配置，
        // 即使沙箱工作区写入失败，下载时仍可从后端获取包含外部配置的完整包。
        if (ShouldPersistArtifactPackages(runtimeContext))
        {
            await PersistIntermediatePackageAsync(runtimeContext, cancellationToken);
        }

        return ApiResponse<HiringExternalSystemConfigDto>.SuccessResponse(
            persistedState?.ToDto(secretProtector) ?? new HiringExternalSystemConfigDto(),
            persistedState is null ? "已清空外部系统配置" : "已保存外部系统配置");
    }

    private async Task<HiringRuntimeContext> EnsureExternalSystemConfigHydratedAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (runtimeContext.ExternalSystemConfig is not null)
        {
            return runtimeContext;
        }

        var sandboxConfig = await LoadExternalSystemConfigFromSandboxAsync(runtimeContext.SandboxId, cancellationToken);
        if (sandboxConfig is null)
        {
            return runtimeContext;
        }

        var hydratedRuntimeContext = runtimeContext with
        {
            ExternalSystemConfig = sandboxConfig
        };
        if (sandboxConfig.IsPersisted)
        {
            if (IsCollectionStageBeforeReadyForPackaging(hydratedRuntimeContext.CurrentStage))
            {
                hydratedRuntimeContext = hydratedRuntimeContext with
                {
                    CurrentStage = HiringCollectionStage.ReadyForPackaging
                };
            }

            hydratedRuntimeContext = MarkPackagingTestCasesWaitingConfirmIfNeeded(hydratedRuntimeContext);
        }

        hydratedRuntimeContext = ApplyConversationProgressToTemplatePackage(hydratedRuntimeContext);
        hiringRuntimeStore.Upsert(hydratedRuntimeContext);
        return hydratedRuntimeContext;
    }

    private static HiringExternalSystemConfigState? DeserializeExternalSystemConfig(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null
            || !metadata.TryGetValue(SandboxMetaKeys.ExternalSystemConfig, out var rawState)
            || string.IsNullOrWhiteSpace(rawState))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HiringExternalSystemConfigState>(rawState, JsonOptions);
        }
        catch (JsonException)
        {
            return TryDeserializeLegacyExternalSystemConfig(rawState);
        }
    }

    private async Task<HiringExternalSystemConfigState?> LoadExternalSystemConfigFromSandboxAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            return null;
        }

        var metadata = await dbContext.SandboxInstances
            .AsNoTracking()
            .Where(item => item.SandboxId == sandboxId)
            .Select(item => item.Metadata)
            .FirstOrDefaultAsync(cancellationToken);

        return DeserializeExternalSystemConfig(metadata);
    }

    private async Task UpsertExternalSystemConfigMetadataAsync(
        string sandboxId,
        HiringExternalSystemConfigState? state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            return;
        }

        var sandboxInstance = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.SandboxId == sandboxId, cancellationToken);
        if (sandboxInstance is null)
        {
            return;
        }

        sandboxInstance.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (state is null)
        {
            sandboxInstance.Metadata.Remove(SandboxMetaKeys.ExternalSystemConfig);
        }
        else
        {
            sandboxInstance.Metadata[SandboxMetaKeys.ExternalSystemConfig] = JsonSerializer.Serialize(state, JsonOptions);
        }

        sandboxInstance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncExternalSystemConfigWorkspaceAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (runtimeContext.ExternalSystemConfig is null
            || !runtimeContext.ExternalSystemConfig.IsPersisted
            || string.IsNullOrWhiteSpace(runtimeContext.SandboxId))
        {
            logger.LogWarning(
                "[MCP-DIAG] SyncExternalSystemConfigWorkspaceAsync skipped. " +
                "ExternalSystemConfig={HasConfig}, IsPersisted={IsPersisted}, SandboxId={SandboxId}, HireId={HireId}",
                runtimeContext.ExternalSystemConfig is not null,
                runtimeContext.ExternalSystemConfig?.IsPersisted ?? false,
                runtimeContext.SandboxId ?? "(null)",
                runtimeContext.HireId);
            return;
        }

        var workspaceFiles = BuildManagedExternalPackageFiles(runtimeContext.ExternalSystemConfig)
            .ToDictionary(
                static pair => pair.Key["external/".Length..],
                static pair => Encoding.UTF8.GetBytes(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        if (workspaceFiles.Count == 0)
        {
            return;
        }

        var uploadResult = await sandboxService.UploadWorkspaceFileAsync(
            new SandboxWorkspaceUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = runtimeContext.HireId,
                SandboxRole = ResolveSandboxRole(runtimeContext.HireId),
                OwnerSubject = runtimeContext.OwnerSubject,
                SandboxId = runtimeContext.SandboxId,
                TargetDir = "external",
                FileName = "external-config-sync.zip",
                Content = BuildArtifactArchive(workspaceFiles),
                ContentType = "application/zip"
            },
            cancellationToken);

        if (!uploadResult.Success)
        {
            logger.LogWarning(
                "Failed to sync external config into sandbox workspace. HireId={HireId}, SandboxId={SandboxId}, Message={Message}",
                runtimeContext.HireId,
                runtimeContext.SandboxId,
                uploadResult.Message);
        }

        // 将用户 MCP 配置合并全局配置后同步到沙箱 MCP 接口，
        // 使 AI Agent 在运行时可调用用户自定义的 MCP 服务器工具
        await SyncMcpConfigToSandboxAsync(runtimeContext, cancellationToken);
    }

    /// <summary>
    /// 将用户 MCP 配置与全局 MCP 配置合并，构建完整的沙箱 MCP 配置（实例入口）。
    /// 通过读取本服务的全局配置与 secretProtector 后委托给可测试的静态实现。
    /// </summary>
    private SandboxWorkspaceMcpConfig BuildMergedMcpConfig(HiringExternalSystemConfigState? externalConfig)
        => BuildMergedMcpConfig(ReadMcpConfig(), externalConfig, secretProtector);

    /// <summary>
    /// 将用户 MCP 配置与全局 MCP 配置合并，构建完整的沙箱 MCP 配置。
    /// 全局配置作为基础，用户配置的服务器以同 ID 合并（覆盖同名服务器）。
    /// 抽取为 internal static 以便单元测试在不构造 EmployeeHiringService 实例的前提下覆盖合并逻辑。
    /// </summary>
    internal static SandboxWorkspaceMcpConfig BuildMergedMcpConfig(
        SandboxWorkspaceMcpConfig? globalConfig,
        HiringExternalSystemConfigState? externalConfig,
        ISecretProtector secretProtector)
    {
        // 1. 以全局配置作为基础；调用方已负责处理 Enabled=false 时返回 null 的语义
        var servers = globalConfig?.Servers?.ToDictionary(
            kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, SandboxMcpServerEntry>();

        // 2. 仅在用户配置已持久化且含有效 MCP 服务器信息时注入；
        //    skipped 模式 IsPersisted=true 但 McpServer 为 null，HasAnyConfig 判断会短路跳过
        if (externalConfig is { IsPersisted: true, McpServer: { HasAnyConfig: true } userMcp })
        {
            var serverId = SanitizeServerId(userMcp.Name);
            servers[serverId] = ConvertToSandboxEntry(userMcp, secretProtector);
        }

        return new SandboxWorkspaceMcpConfig
        {
            Enabled = servers.Count > 0,
            Servers = servers
        };
    }

    /// <summary>
    /// 将用户配置的 MCP 服务器转换为 Kingcrab 沙箱识别的配置项（实例入口）。
    /// </summary>
    private SandboxMcpServerEntry ConvertToSandboxEntry(HiringMcpServerConfigState userMcp)
        => ConvertToSandboxEntry(userMcp, secretProtector);

    /// <summary>
    /// 将用户配置的 MCP 服务器转换为 Kingcrab 沙箱识别的配置项。
    /// stdio 与 HTTP 两种传输模式下需要的字段互斥，需仅填充对应模式的字段以避免误启动。
    /// </summary>
    internal static SandboxMcpServerEntry ConvertToSandboxEntry(
        HiringMcpServerConfigState userMcp,
        ISecretProtector secretProtector)
    {
        var isStdio = string.Equals(userMcp.Transport, "stdio", StringComparison.OrdinalIgnoreCase);

        // 解密环境变量：ProtectedEnv 存储的是加密后的越界值，运行时需还原为明文供沙箱进程使用
        Dictionary<string, string>? environment = null;
        if (userMcp.ProtectedEnv.Count > 0)
        {
            environment = new Dictionary<string, string>();
            foreach (var (key, encryptedValue) in userMcp.ProtectedEnv)
            {
                var decrypted = secretProtector.Unprotect(encryptedValue);
                if (!string.IsNullOrEmpty(decrypted))
                {
                    environment[key] = decrypted;
                }
            }
        }

        // 解密请求头：HTTP 模式下同时处理加密头、明文环境头、Bearer Token三种来源
        Dictionary<string, string>? headers = null;
        if (!isStdio)
        {
            headers = new Dictionary<string, string>();

            // 加密头：需要解密后发送到 MCP 服务端
            foreach (var (key, encryptedValue) in userMcp.ProtectedHeaders)
            {
                var decrypted = secretProtector.Unprotect(encryptedValue);
                if (!string.IsNullOrEmpty(decrypted))
                {
                    headers[key] = decrypted;
                }
            }

            // 明文环境变量处理后的头：前端已将环境变量值读取为明文，直接注入即可
            foreach (var (key, value) in userMcp.HeadersFromEnv)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    headers[key] = value;
                }
            }

            // BearerTokenEnv 存储的是加密后的 Token，解密后以 Bearer 方式注入 Authorization 头
            if (!string.IsNullOrWhiteSpace(userMcp.BearerTokenEnv))
            {
                var bearerToken = secretProtector.Unprotect(userMcp.BearerTokenEnv);
                if (!string.IsNullOrEmpty(bearerToken))
                {
                    headers["Authorization"] = $"Bearer {bearerToken}";
                }
            }

            if (headers.Count == 0)
            {
                headers = null;
            }
        }

        // Transport 映射：前端使用 "http" 标识，Kingcrab 沙箱要求使用 "streamable-http"
        var transport = userMcp.Transport;
        if (string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
        {
            transport = "streamable-http";
        }

        var sanitizedName = SanitizeServerId(userMcp.Name);

        return new SandboxMcpServerEntry
        {
            Transport = transport,
            Url = isStdio ? string.Empty : userMcp.Url,
            Enabled = true,
            ToolNamePrefix = $"{sanitizedName}.",
            Name = userMcp.Name,
            Command = isStdio ? userMcp.Command : null,
            Arguments = isStdio && userMcp.Args.Count > 0 ? userMcp.Args : null,
            WorkingDirectory = isStdio ? (string.IsNullOrWhiteSpace(userMcp.Cwd) ? null : userMcp.Cwd) : null,
            Environment = isStdio ? environment : null,
            Headers = headers,
            StartupTimeoutSeconds = 15,
            RequestTimeoutSeconds = 60
        };
    }

    /// <summary>
    /// 清理服务器 ID：仅保留字母数字、连字符与下划线，统一转为小写。
    /// 避免中文/特殊字符造成 MCP 服务器 Key 冲突或不合法。
    /// </summary>
    internal static string SanitizeServerId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "user-mcp";
        }

        var sanitized = new string(name
            .Where(static c => (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '-' || c == '_')
            .ToArray())
            .ToLowerInvariant()
            .Trim('-', '_');

        return string.IsNullOrEmpty(sanitized) ? "user-mcp" : sanitized;
    }

    /// <summary>
    /// 将合并后的 MCP 配置同步到沙箱，使 AI Agent 能发现并使用 MCP 工具。
    /// 上传为非致命操作，失败仅记录警告以不阻断主保存流程。
    /// </summary>
    private async Task SyncMcpConfigToSandboxAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeContext.SandboxId))
        {
            logger.LogWarning("[MCP-DIAG] SyncMcpConfigToSandboxAsync skipped: SandboxId is empty. HireId={HireId}", runtimeContext.HireId);
            return;
        }

        var mergedConfig = BuildMergedMcpConfig(runtimeContext.ExternalSystemConfig);
        if (!mergedConfig.Enabled || mergedConfig.Servers.Count == 0)
        {
            logger.LogWarning(
                "[MCP-DIAG] SyncMcpConfigToSandboxAsync skipped: Enabled={Enabled}, ServerCount={ServerCount}, HireId={HireId}",
                mergedConfig.Enabled, mergedConfig.Servers.Count, runtimeContext.HireId);
            return;
        }

        var result = await UploadSandboxMcpConfigAsync(
            runtimeContext.HireId,
            runtimeContext.OwnerSubject,
            mergedConfig,
            cancellationToken);

        if (!result.Success)
        {
            logger.LogWarning(
                "Failed to sync user MCP config to sandbox. HireId={HireId}, StatusCode={StatusCode}, Message={Message}",
                runtimeContext.HireId, result.StatusCode, result.Message);
        }
        else
        {
            logger.LogInformation(
                "User MCP config synced to sandbox successfully. HireId={HireId}, ServerCount={ServerCount}",
                runtimeContext.HireId, mergedConfig.Servers.Count);
        }
    }

    private static HiringExternalSystemConfigState? TryDeserializeLegacyExternalSystemConfig(string rawState)
    {
        try
        {
            var legacyState = JsonSerializer.Deserialize<LegacyHiringExternalSystemConfigState>(rawState, JsonOptions);
            if (legacyState is null)
            {
                return null;
            }

            var cliTools = (legacyState.CliTools ?? [])
                .Where(static tool => !string.IsNullOrWhiteSpace(tool.ToolName))
                .Select(static tool => new HiringCliToolConfigState(
                    Name: tool.ToolName.Trim(),
                    Command: string.Empty,
                    Description: tool.Description?.Trim() ?? string.Empty,
                    ExecutionMode: string.Equals(tool.ExecutionMode, "sandbox", StringComparison.OrdinalIgnoreCase) ? "sandbox" : "direct",
                    Parameters: null))
                .ToArray();

            HiringMcpServerConfigState? mcpServer = null;
            if (legacyState.McpServer is not null
                && !string.IsNullOrWhiteSpace(legacyState.McpServer.ServerUrl))
            {
                mcpServer = new HiringMcpServerConfigState(
                    Transport: "http",
                    Name: "legacy-mcp",
                    Command: string.Empty,
                    Args: [],
                    ProtectedEnv: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    EnvPassThrough: [],
                    Cwd: string.Empty,
                    Url: legacyState.McpServer.ServerUrl.Trim(),
                    BearerTokenEnv: string.Empty,
                    ProtectedHeaders: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    HeadersFromEnv: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }

            var state = new HiringExternalSystemConfigState(
                SubmissionMode: HiringExternalSystemSubmissionModes.Configured,
                CliTools: cliTools,
                McpServer: mcpServer,
                UpdatedAtUtc: legacyState.UpdatedAtUtc ?? DateTimeOffset.UtcNow);

            return state.IsPersisted ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LegacyHiringExternalSystemConfigState
    {
        public IReadOnlyList<LegacyHiringCliToolConfigDto> CliTools { get; init; } = [];

        public LegacyHiringMcpServerConfigState? McpServer { get; init; }

        public DateTimeOffset? UpdatedAtUtc { get; init; }
    }

    private sealed record LegacyHiringCliToolConfigDto
    {
        public string ToolName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string ExecutionMode { get; init; } = "direct";
    }

    private sealed record LegacyHiringMcpServerConfigState
    {
        public string ServerUrl { get; init; } = string.Empty;
    }
}
