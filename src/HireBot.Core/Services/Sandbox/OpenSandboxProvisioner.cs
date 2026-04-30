using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSandbox;
using OpenSandbox.Config;
using OpenSandbox.Models;

namespace HireBot.Core.Services.Sandbox;

internal sealed class OpenSandboxProvisioner(
    IConfiguration configuration,
    IServiceScopeFactory serviceScopeFactory,
    SandboxPvcService pvcService,
    ILogger<OpenSandboxProvisioner> logger)
{
    public SandboxProvisioningSettings GetSettings() => SandboxProvisioningSettings.FromConfiguration(configuration);

    /// <summary>
    /// 创建一个新的 OpenSandbox 沙箱实例。
    /// 会为 ownerSubject 确保 PVC 存储卷，并将 ownerSubject 作为 Kubernetes metadata 写入。
    /// 注意：ownerSubject 中的特殊字符（如冒号）会通过 ToK8sLabelValue 转义，以满足 K8s label value 规范。
    /// </summary>
    /// <param name="ownerSubject">沙箱所有者标识，可能包含 "tenant:operator" 格式</param>
    /// <returns>新创建的沙箱 ID、初始状态和网关地址</returns>
    public async Task<ProvisionedSandboxResult> CreateAsync(string ownerSubject, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        var volumes = await pvcService.EnsureUserPvcsAsync(ownerSubject, cancellationToken);
        var createOptions = BuildCreateOptions(settings, ownerSubject, volumes);

        logger.LogInformation(
            "创建 OpenSandbox 沙箱. Domain={Domain}, Image={Image}, GatewayPort={GatewayPort}, UseServerProxy={UseServerProxy}, Owner={OwnerSubject}",
            settings.Domain,
            settings.Image,
            settings.GatewayPort,
            settings.UseServerProxy,
            ownerSubject);

        await using var sandbox = await global::OpenSandbox.Sandbox.CreateAsync(createOptions, cancellationToken);

        return new ProvisionedSandboxResult(
            sandbox.Id,
            "Creating",
            null,
            null);
    }

    internal static SandboxCreateOptions BuildCreateOptions(
        SandboxProvisioningSettings settings,
        string ownerSubject,
        IReadOnlyList<Volume> volumes)
    {
        var connection = settings.BuildConnection();
        var env = settings.BuildRuntimeEnv();

        return new SandboxCreateOptions
        {
            ConnectionConfig = connection,
            Image = settings.Image,
            Resource = settings.ResourceLimits,
            TimeoutSeconds = settings.TimeoutSeconds,
            ReadyTimeoutSeconds = settings.ReadyTimeoutSeconds,
            Entrypoint = [.. settings.Entrypoint],
            Env = env,
            NetworkPolicy = settings.BuildNetworkPolicy(),
            Volumes = volumes.Count > 0 ? volumes : null,
            // Kubernetes label value 不允许冒号等特殊字符，且长度不超过 63 字符。
            // ownerSubject 可能包含 "tenant:operator" 格式，需要规范化后再传入。
            Metadata = new Dictionary<string, string> { ["owner"] = ToK8sLabelValue(ownerSubject) },
            ManualCleanup = true,
            // HireBot 自己会在 CreateAsync 之后继续轮询 Running + GatewayEndpoint，
            // 这里跳过 SDK 的前置健康检查，避免 OpenClaw 网关慢启动时直接把创建阶段打成 502。
            SkipHealthCheck = true
        };
    }

    /// <summary>
    /// 启动后台任务，异步同步沙箱状态到数据库，直到沙箱进入终态（Running/Stopped/Error/Paused）。
    /// 每 5 秒轮询一次，最多轮询 60 次（5 分钟）。
    /// </summary>
    public Task BeginTrackingAsync(Guid instanceId, string sandboxId)
    {
        _ = SyncStateAsync(instanceId, sandboxId, GetSettings(), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 OpenSandbox 刷新指定沙箱的最新状态和网关地址。
    /// 当沙箱处于 Running 状态时，会额外解析 GatewayEndpoint。
    /// </summary>
    public async Task<ProvisionedSandboxResult> RefreshAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        var connection = settings.BuildConnection();
        var http = connection.GetHttpClient();
        var baseUrl = connection.GetBaseUrl().TrimEnd('/');

        var response = await http.GetAsync($"{baseUrl}/sandboxes/{Uri.EscapeDataString(sandboxId)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var state = ExtractState(root);
        DateTimeOffset? expiresAtUtc = null;
        if (root.TryGetProperty("expiresAt", out var expiresElement) && expiresElement.TryGetDateTime(out var expiresAt))
        {
            expiresAtUtc = new DateTimeOffset(expiresAt, TimeSpan.Zero);
        }

        var gatewayEndpoint = state == "Running"
            ? await ResolveGatewayEndpointAsync(http, baseUrl, sandboxId, settings.GatewayPort, settings.UseServerProxy, cancellationToken)
            : null;

        return new ProvisionedSandboxResult(sandboxId, state, gatewayEndpoint, expiresAtUtc);
    }

    public async Task<string?> GetGatewayEndpointAsync(
        string sandboxId,
        bool useServerProxy,
        CancellationToken cancellationToken = default)
    {
        var result = await GetGatewayEndpointResultAsync(sandboxId, useServerProxy, cancellationToken);
        return result.Success ? result.Data : null;
    }

    public async Task<RemoteCallResult<string>> GetGatewayEndpointResultAsync(
        string sandboxId,
        bool useServerProxy,
        CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        var connection = settings.BuildConnection();
        var http = connection.GetHttpClient();
        var baseUrl = connection.GetBaseUrl().TrimEnd('/');

        return await ResolveGatewayEndpointResultAsync(
            http,
            baseUrl,
            sandboxId,
            settings.GatewayPort,
            useServerProxy,
            cancellationToken);
    }

    public async Task PauseAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        await SendLifecycleCommandAsync(settings.BuildConnection(), sandboxId, "pause", cancellationToken);
    }

    public async Task ResumeAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        await SendLifecycleCommandAsync(settings.BuildConnection(), sandboxId, "resume", cancellationToken);
    }

    public async Task DeleteAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        var connection = settings.BuildConnection();
        var http = connection.GetHttpClient();
        await http.DeleteAsync($"{connection.GetBaseUrl().TrimEnd('/')}/sandboxes/{Uri.EscapeDataString(sandboxId)}", cancellationToken);
    }

    public async Task<ProvisionedSandboxResult> RebuildAsync(string ownerSubject, string sandboxId, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        var connection = settings.BuildConnection();
        var http = connection.GetHttpClient();
        var baseUrl = connection.GetBaseUrl().TrimEnd('/');

        await http.DeleteAsync($"{baseUrl}/sandboxes/{Uri.EscapeDataString(sandboxId)}", cancellationToken);
        return await CreateAsync(ownerSubject, cancellationToken);
    }

    private async Task SendLifecycleCommandAsync(ConnectionConfig connection, string sandboxId, string action, CancellationToken cancellationToken)
    {
        var http = connection.GetHttpClient();
        var baseUrl = connection.GetBaseUrl().TrimEnd('/');
        using var response = await http.PostAsync($"{baseUrl}/sandboxes/{Uri.EscapeDataString(sandboxId)}/{action}", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SyncStateAsync(Guid instanceId, string sandboxId, SandboxProvisioningSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var connection = settings.BuildConnection();
            var http = connection.GetHttpClient();
            var baseUrl = connection.GetBaseUrl().TrimEnd('/');

            for (var attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                using var response = await http.GetAsync($"{baseUrl}/sandboxes/{Uri.EscapeDataString(sandboxId)}", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = doc.RootElement;
                var state = ExtractState(root);

                string? endpoint = null;
                if (state == "Running")
                {
                    endpoint = await ResolveGatewayEndpointAsync(http, baseUrl, sandboxId, settings.GatewayPort, settings.UseServerProxy, cancellationToken);
                }

                DateTimeOffset? expiresAtUtc = null;
                if (root.TryGetProperty("expiresAt", out var expiresElement) && expiresElement.TryGetDateTime(out var expiresAt))
                {
                    expiresAtUtc = new DateTimeOffset(expiresAt, TimeSpan.Zero);
                }

                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<HireBotDbContext>();
                var instance = await dbContext.SandboxInstances.FirstOrDefaultAsync(item => item.Id == instanceId, cancellationToken);
                if (instance is null)
                {
                    return;
                }

                instance.State = state;
                instance.GatewayEndpoint = endpoint;
                instance.ExpiresAtUtc = expiresAtUtc;
                instance.LastError = null;
                instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                if (state is "Running" or "Stopped" or "Error" or "Paused")
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "后台同步 OpenSandbox 状态失败. SandboxId={SandboxId}", sandboxId);
            await TryRecordErrorAsync(instanceId, ex.Message, CancellationToken.None);
        }
    }

    private async Task TryRecordErrorAsync(Guid instanceId, string error, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HireBotDbContext>();
            var instance = await dbContext.SandboxInstances.FirstOrDefaultAsync(item => item.Id == instanceId, cancellationToken);
            if (instance is null)
            {
                return;
            }

            instance.LastError = error;
            instance.State = "Error";
            instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Ignore background sync error persistence failures.
        }
    }

    private static string ExtractState(System.Text.Json.JsonElement root)
    {
        return root.TryGetProperty("status", out var statusElement) &&
               statusElement.TryGetProperty("state", out var stateElement)
            ? stateElement.GetString() ?? "Unknown"
            : "Unknown";
    }

    internal static string BuildEndpointLookupUrl(
        string baseUrl,
        string sandboxId,
        int gatewayPort,
        bool useServerProxy)
    {
        var endpointUrl = $"{baseUrl.TrimEnd('/')}/sandboxes/{Uri.EscapeDataString(sandboxId)}/endpoints/{gatewayPort}";
        return useServerProxy
            ? $"{endpointUrl}?use_server_proxy=true"
            : endpointUrl;
    }

    private static async Task<string?> ResolveGatewayEndpointAsync(
        HttpClient http,
        string baseUrl,
        string sandboxId,
        int gatewayPort,
        bool useServerProxy,
        CancellationToken cancellationToken)
    {
        var result = await ResolveGatewayEndpointResultAsync(
            http,
            baseUrl,
            sandboxId,
            gatewayPort,
            useServerProxy,
            cancellationToken);

        return result.Success ? result.Data : null;
    }

    private static async Task<RemoteCallResult<string>> ResolveGatewayEndpointResultAsync(
        HttpClient http,
        string baseUrl,
        string sandboxId,
        int gatewayPort,
        bool useServerProxy,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            BuildEndpointLookupUrl(baseUrl, sandboxId, gatewayPort, useServerProxy),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return RemoteCallResult<string>.Failure(
                (int)response.StatusCode,
                $"OpenSandbox endpoint lookup failed (HTTP {(int)response.StatusCode})");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("endpoint", out var endpointElement) ||
            string.IsNullOrWhiteSpace(endpointElement.GetString()))
        {
            return RemoteCallResult<string>.Failure(502, "OpenSandbox endpoint lookup returned an empty endpoint");
        }

        return RemoteCallResult<string>.Ok(endpointElement.GetString()!.Trim());
    }

    /// <summary>
    /// 将任意字符串规范化为合法的 Kubernetes label value。
    /// K8s label value 规则：只允许字母、数字、连字符(-)、下划线(_)、点(.)，
    /// 必须以字母或数字开头和结尾，且长度不超过 63 字符。
    /// 典型场景：ownerSubject 可能包含 "tenant-default:operator-default" 格式，冒号需要替换。
    /// </summary>
    private static string ToK8sLabelValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        // 将不合法字符（如冒号、斜杠、@等）替换为连字符
        var normalized = System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"[^a-zA-Z0-9\-_.]", "-");

        // 去掉首尾非字母数字字符
        normalized = normalized.TrimStart('-', '_', '.').TrimEnd('-', '_', '.');

        if (string.IsNullOrEmpty(normalized))
        {
            return "unknown";
        }

        // 截断到 63 字符，并确保末尾是字母或数字
        if (normalized.Length > 63)
        {
            normalized = normalized[..63].TrimEnd('-', '_', '.');
        }

        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }

    internal sealed record ProvisionedSandboxResult(
        string SandboxId,
        string State,
        string? GatewayEndpoint,
        DateTimeOffset? ExpiresAtUtc);
}
