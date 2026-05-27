namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringExternalSystemConfigDto
{
    public IReadOnlyList<HiringCliToolConfigDto> CliTools { get; init; } = [];

    public HiringMcpServerConfigDto? McpServer { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

public sealed record HiringCliToolConfigDto
{
    public string ToolName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string ExecutionMode { get; init; } = "direct";

    public string ArgumentTemplate { get; init; } = string.Empty;
}

public sealed record HiringMcpServerConfigDto
{
    public string ServerUrl { get; init; } = string.Empty;

    public string AuthMode { get; init; } = "none";

    public string ApiKey { get; init; } = string.Empty;

    public bool HasApiKey { get; init; }

    public IReadOnlyList<string> SelectedTools { get; init; } = [];
}
