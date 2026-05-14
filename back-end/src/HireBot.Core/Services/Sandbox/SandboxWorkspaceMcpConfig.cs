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
}
