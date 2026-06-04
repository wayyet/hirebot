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

    /// <summary>对话上传文件列表（仅元数据，不含文件内容）。</summary>
    public IReadOnlyList<PersistedChatFileDto>? UploadedFiles { get; init; }

    /// <summary>最新产物包结构（文件名 + 包内文件列表）。</summary>
    public PersistedPackageStructureDto? PackageStructure { get; init; }
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
/// 可持久化的对话上传文件元数据（不含 rawFile / content 等大字段）。
/// </summary>
public sealed record PersistedChatFileDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? MimeType { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// 最新产物包结构（ZIP 文件名 + 包内文件路径列表 + 生成的员工实例 ID）。
/// </summary>
public sealed record PersistedPackageStructureDto
{
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> FileNames { get; init; } = [];
    /// <summary>importPackage 返回的员工实例 ID，用于刷新后恢复 AI 评估入口。</summary>
    public string? EmployeeId { get; init; }
}

/// <summary>
/// 雇佣运行时状态响应 DTO。
/// </summary>
public sealed record RuntimeStateDto
{
    public IReadOnlyDictionary<string, object>? StageOverrides { get; init; }
    public IReadOnlyDictionary<string, DownstreamRunInfo>? DownstreamRuns { get; init; }

    /// <summary>对话上传文件列表（仅元数据）。</summary>
    public IReadOnlyList<PersistedChatFileDto>? UploadedFiles { get; init; }

    /// <summary>最新产物包结构。</summary>
    public PersistedPackageStructureDto? PackageStructure { get; init; }
}
