namespace HireBot.Abstraction.Models.Hiring;

/// <summary>
/// 批量保存阶段覆盖配置和下游运行记录请求 DTO。
/// </summary>
public sealed record SaveRuntimeStateRequestDto
{
    /// <summary>阶段覆盖配置（Map 格式）。</summary>
    public IReadOnlyDictionary<string, object>? StageOverrides { get; init; }

    /// <summary>下游运行记录（Map 格式）。</summary>
    public IReadOnlyDictionary<string, DownstreamRunInfo>? DownstreamRuns { get; init; }
}

/// <summary>
/// 下游运行信息。
/// </summary>
public sealed record DownstreamRunInfo
{
    public string Status { get; init; } = string.Empty;
    public object? Result { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 雇佣运行时状态响应 DTO。
/// </summary>
public sealed record RuntimeStateDto
{
    public IReadOnlyDictionary<string, object>? StageOverrides { get; init; }
    public IReadOnlyDictionary<string, DownstreamRunInfo>? DownstreamRuns { get; init; }
}
