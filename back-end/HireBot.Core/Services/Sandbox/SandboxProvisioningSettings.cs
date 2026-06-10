using Microsoft.Extensions.Configuration;
using OpenSandbox.Config;
using OpenSandbox.Models;

namespace HireBot.Core.Services.Sandbox;

internal sealed record SandboxProvisioningSettings(
    string Domain,
    ConnectionProtocol Protocol,
    bool UseServerProxy,
    string ApiKey,
    string Image,
    IReadOnlyDictionary<string, string> ResourceLimits,
    int TimeoutSeconds,
    int ReadyTimeoutSeconds,
    int RequestTimeoutSeconds,
    int GatewayPort,
    IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> AllowedOrigins,
    string AuthToken,
    string OidcAuthority,
    string OidcAudience,
    string ToolTimeoutSeconds,
    string LlmProvider,
    string LlmModel,
    string LlmEndpoint,
    string LlmApiKey,
    float LlmTemperature,
    bool LlmEnableThinking,
    IReadOnlyList<string> NetworkEgressAllowHosts,
    IReadOnlyDictionary<string, int> DefaultTimeoutSecondsByRole)
{
    public static SandboxProvisioningSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var domain = configuration["OpenSandbox:Domain"];
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("OpenSandbox:Domain is required.");
        }

        var image = configuration["OpenSandbox:Image"];
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException("OpenSandbox:Image is required.");
        }

        var gatewayPort = configuration.GetValue("OpenSandbox:GatewayPort", 0);
        if (gatewayPort <= 0)
        {
            throw new InvalidOperationException("OpenSandbox:GatewayPort must be greater than zero.");
        }

        var timeoutSeconds = configuration.GetValue("OpenSandbox:TimeoutSeconds", 0);
        if (timeoutSeconds <= 0)
        {
            throw new InvalidOperationException("OpenSandbox:TimeoutSeconds must be greater than zero.");
        }

        var readyTimeoutSeconds = configuration.GetValue("OpenSandbox:ReadyTimeoutSeconds", 0);
        if (readyTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("OpenSandbox:ReadyTimeoutSeconds must be greater than zero.");
        }

        // Alibaba.OpenSandbox SDK HttpClient default is 30s; sandbox POST / create often exceeds that when pulling images or scheduling pods.
        var requestTimeoutSeconds = configuration.GetValue("OpenSandbox:RequestTimeoutSeconds", 0);
        if (requestTimeoutSeconds < 60)
        {
            requestTimeoutSeconds = Math.Max(readyTimeoutSeconds + 120, 300);
        }

        var allowedOrigins = configuration.GetSection("AllowedOrigins:Sandbox").Get<string[]>()
            ?? configuration.GetSection("AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173"];

        var protocol = Enum.Parse<ConnectionProtocol>(
            configuration["OpenSandbox:Protocol"] ?? "Http",
            ignoreCase: true);
        var useServerProxy = configuration.GetValue("OpenSandbox:UseServerProxy", false);

        var resourceLimits = configuration.GetSection("OpenSandbox:Resource").Get<Dictionary<string, string>>();
        if (resourceLimits is null || resourceLimits.Count == 0)
        {
            resourceLimits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cpu"] = "500m",
                ["memory"] = "1Gi"
            };
        }

        var entrypoint = configuration.GetSection("OpenSandbox:Entrypoint").Get<string[]>() ?? ["/app/OpenClaw.Gateway"];
        var egressHosts = configuration.GetSection("OpenSandbox:KingCrab:NetworkEgressAllowHosts").Get<string[]>() ?? [];

        // 按角色设置沙箱默认超时时间（秒）。hiring / evaluation 类角色应设置 TTL，runtime 角色不配置则保持手动清理。
        var timeoutByRole = configuration.GetSection("OpenSandbox:DefaultTimeoutSecondsByRole")
            .Get<Dictionary<string, int>>()
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["hiring"] = 1800,
                ["evaluation-target"] = 1800,
                ["evaluation-evaluator"] = 1800
            };

        var apiKey = configuration["OpenSandbox:ApiKey"] ?? configuration["OPEN_SANDBOX_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenSandbox API key not configured. Set OpenSandbox:ApiKey or OPEN_SANDBOX_API_KEY.");
        }

        return new SandboxProvisioningSettings(
            domain.Trim(),
            protocol,
            useServerProxy,
            apiKey,
            image.Trim(),
            resourceLimits,
            timeoutSeconds,
            readyTimeoutSeconds,
            requestTimeoutSeconds,
            gatewayPort,
            entrypoint,
            allowedOrigins,
            configuration["OpenSandbox:KingCrab:AuthToken"] ?? "king-crab-sandbox-token",
            configuration["OpenSandbox:KingCrab:OidcAuthority"] ?? string.Empty,
            configuration["OpenSandbox:KingCrab:OidcAudience"] ?? "account",
            configuration["OpenSandbox:KingCrab:ToolTimeoutSeconds"] ?? "300",
            configuration["OpenSandbox:KingCrab:LlmProvider"] ?? "openai",
            configuration["OpenSandbox:KingCrab:LlmModel"] ?? string.Empty,
            configuration["OpenSandbox:KingCrab:LlmEndpoint"] ?? string.Empty,
            configuration["OpenSandbox:KingCrab:LlmApiKey"] ?? string.Empty,
            configuration.GetValue("OpenSandbox:KingCrab:LlmTemperature", 0.7f),
            configuration.GetValue("OpenSandbox:KingCrab:LlmEnableThinking", false),
            egressHosts,
            timeoutByRole);
    }

    /// <summary>
    /// 按沙箱角色返回配置的 TTL（秒）。角色未在字典中配置时返回 null，表示不设超时（由调用方决定启用 ManualCleanup）。
    /// </summary>
    public int? GetTimeoutSecondsForRole(string sandboxRole)
    {
        if (string.IsNullOrWhiteSpace(sandboxRole))
        {
            return null;
        }

        return DefaultTimeoutSecondsByRole.TryGetValue(sandboxRole, out var seconds) ? seconds : null;
    }

    public ConnectionConfig BuildConnection()
    {
        return new ConnectionConfig(new ConnectionConfigOptions
        {
            Domain = Domain,
            Protocol = Protocol,
            UseServerProxy = UseServerProxy,
            RequestTimeoutSeconds = RequestTimeoutSeconds,
            ApiKey = ApiKey
        });
    }

    public Dictionary<string, string> BuildRuntimeEnv()
    {
        ValidateGatewayModelProviderConfiguration();

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Logging__LogLevel__Default"] = "Information",
            ["Logging__LogLevel__Microsoft"] = "Warning",
            ["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning",
            ["OpenClaw__BindAddress"] = "0.0.0.0",
            ["OpenClaw__Port"] = GatewayPort.ToString(),
            ["OpenClaw__AuthToken"] = AuthToken,
            ["OpenClaw__Security__AlwaysRequireAuth"] = "true",
            ["OpenClaw__Security__AllowQueryStringToken"] = "true",
            ["OpenClaw__Security__OidcAuthority"] = OidcAuthority,
            ["OpenClaw__Security__OidcAudience"] = OidcAudience,
            ["OpenClaw__Security__OidcRequireHttpsMetadata"] = "false",
            ["OpenClaw__Security__AllowUnsafeToolingOnPublicBind"] = "true",
            ["OpenClaw__Security__AllowPluginBridgeOnPublicBind"] = "true",
            ["OpenClaw__Security__AllowRawSecretRefsOnPublicBind"] = "true",
            // Canvas 默认 Enabled=true，但 AllowOnPublicBind=false，绑定 0.0.0.0 时会触发启动拒绝异常。
            // 沙箱容器场景下显式 opt-in，保留 Canvas 功能。
            ["OpenClaw__Canvas__AllowOnPublicBind"] = "true",
            ["OpenClaw__Plugins__Enabled"] = "true",
            ["OpenClaw__Plugins__Mcp__Enabled"] = "true",
            ["OpenClaw__Tooling__AllowShell"] = "true",
            ["OpenClaw__Tooling__Presets__coding__Description"] = "sandbox-default",
            ["OpenClaw__Tooling__SurfaceBindings__websocket"] = "full",
            ["OpenClaw__Tooling__WorkspaceRoot"] = "/workspace",
            ["OpenClaw__Tooling__WorkspaceOnly"] = "false",
            ["OpenClaw__Tooling__AllowBrowserEvaluate"] = "true",
            ["OpenClaw__Tooling__EnablePublishFile"] = "false",
            ["OpenClaw__Tooling__ToolTimeoutSeconds"] = ToolTimeoutSeconds,
            ["OpenClaw__Tooling__ToolApprovalTimeoutSeconds"] = "300",
            ["OpenClaw__Tooling__AllowedReadRoots__0"] = "*",
            ["OpenClaw__Tooling__AllowedWriteRoots__0"] = "*",
            ["OPENCLAW_WORKSPACE"] = "/workspace",
            ["OpenClaw__Memory__StoragePath"] = "/app/memory",
            ["OpenClaw__Memory__MaxHistoryTurns"] = "50",
            ["OpenClaw__Memory__CompactionThreshold"] = "60",
            ["MODEL_PROVIDER_KEY"] = LlmApiKey,
            ["MODEL_PROVIDER_MODEL"] = LlmModel,
            ["MODEL_PROVIDER_ENDPOINT"] = LlmEndpoint,
            // 通过 ASP.NET Core 配置覆盖沙箱容器内置的 appsettings.Production.json LLM 配置
            ["OpenClaw__Llm__Provider"] = LlmProvider,
            ["OpenClaw__Llm__Model"] = LlmModel,
            ["OpenClaw__Llm__ApiKey"] = LlmApiKey,
            ["OpenClaw__Llm__Endpoint"] = LlmEndpoint,
            ["OpenClaw__Llm__Temperature"] = LlmTemperature.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
            ["OpenClaw__Llm__EnableThinking"] = LlmEnableThinking.ToString().ToLowerInvariant()
        };

        for (var i = 0; i < AllowedOrigins.Count; i++)
        {
            env[$"OpenClaw__Security__AllowedOrigins__{i}"] = AllowedOrigins[i];
        }

        return env;
    }

    private void ValidateGatewayModelProviderConfiguration()
    {
        var hasModel = !string.IsNullOrWhiteSpace(LlmModel);
        var hasEndpoint = !string.IsNullOrWhiteSpace(LlmEndpoint);
        var hasApiKey = !string.IsNullOrWhiteSpace(LlmApiKey);

        if (hasModel && hasEndpoint && hasApiKey)
        {
            return;
        }

        if (!hasModel && !hasEndpoint && !hasApiKey)
        {
            throw new InvalidOperationException(
                "OpenSandbox:KingCrab:LlmModel, OpenSandbox:KingCrab:LlmEndpoint, and OpenSandbox:KingCrab:LlmApiKey are required for managed sandbox conversations.");
        }

        throw new InvalidOperationException(
            "OpenSandbox:KingCrab:LlmModel, OpenSandbox:KingCrab:LlmEndpoint, and OpenSandbox:KingCrab:LlmApiKey must be configured together.");
    }

    public NetworkPolicy? BuildNetworkPolicy()
    {
        if (NetworkEgressAllowHosts.Count == 0)
        {
            return null;
        }

        return new NetworkPolicy
        {
            DefaultAction = NetworkRuleAction.Allow,
            Egress =
            [
                .. NetworkEgressAllowHosts.Select(host => new NetworkRule
                {
                    Action = NetworkRuleAction.Allow,
                    Target = host
                })
            ]
        };
    }
}
