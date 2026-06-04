using System.Text.Json;

namespace HireBot.Abstraction.Models.Hiring;

public static class HiringExternalSystemSubmissionModes
{
    public const string Pending = "pending";
    public const string Configured = "configured";
    public const string Skipped = "skipped";
}

public sealed record HiringExternalSystemConfigDto
{
    public string SubmissionMode { get; init; } = HiringExternalSystemSubmissionModes.Pending;

    public IReadOnlyList<HiringCliToolConfigDto> CliTools { get; init; } = [];

    /// <summary>MCP 服务列表（支持多项）。</summary>
    public IReadOnlyList<HiringMcpServerConfigDto> McpServers { get; init; } = [];

    /// <summary>已废弃，仅用于向后兼容旧数据反序列化，新写入请使用 McpServers。</summary>
    [Obsolete("Use McpServers instead.")]
    public HiringMcpServerConfigDto? McpServer { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

public sealed record HiringCliToolConfigDto
{
    public string Name { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string ExecutionMode { get; init; } = "direct";

    public JsonElement? Parameters { get; init; }
}

public sealed record HiringMcpServerConfigDto
{
    public string Transport { get; init; } = "streamable-http";

    public string Name { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];

    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> EnvPassThrough { get; init; } = [];

    public string Cwd { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string BearerTokenEnv { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> HeadersFromEnv { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
