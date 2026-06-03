using System.Text.Json.Serialization;

namespace HireBot.Core.Services.Sandbox;

/// <summary>
/// 沙箱工作空间 MCP（Model Context Protocol）配置，与 OpenSandbox 的 /admin/workspace/mcp 接口 payload 一一对应。
/// 通过 appsettings 中的 OpenSandbox:McpConfig 节进行配置；Enabled=false 时跳过上传。
/// </summary>
internal sealed class SandboxWorkspaceMcpConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("Servers")]
    public Dictionary<string, SandboxMcpServerEntry> Servers { get; init; } = [];
}

/// <summary>单个 MCP 服务器的配置项。</summary>
internal sealed class SandboxMcpServerEntry
{
    [JsonPropertyName("Transport")]
    public string Transport { get; init; } = string.Empty;

    [JsonPropertyName("Url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("Enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("ToolNamePrefix")]
    public string? ToolNamePrefix { get; init; }

    [JsonPropertyName("StartupTimeoutSeconds")]
    public int StartupTimeoutSeconds { get; init; } = 15;

    [JsonPropertyName("RequestTimeoutSeconds")]
    public int RequestTimeoutSeconds { get; init; } = 60;

    // 以下字段用于支持用户配置的 MCP 服务器（含 stdio 启动参数与 HTTP 鉴权头），
    // 全局配置仅依赖 Transport/Url，因此使用可空类型避免序列化空对象。
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Command")]
    public string? Command { get; init; }

    [JsonPropertyName("Arguments")]
    public IReadOnlyList<string>? Arguments { get; init; }

    [JsonPropertyName("WorkingDirectory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("Environment")]
    public Dictionary<string, string>? Environment { get; init; }

    [JsonPropertyName("Headers")]
    public Dictionary<string, string>? Headers { get; init; }
}
