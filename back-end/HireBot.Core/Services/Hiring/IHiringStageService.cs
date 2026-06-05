using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 雇佣阶段状态服务（替代 HiringRuntimeStore，轻量化设计）。
/// </summary>
internal interface IHiringStageService
{
    /// <summary>获取雇佣阶段进度。</summary>
    Task<HiringStageProgressDto?> GetStageProgressAsync(string hireId, CancellationToken cancellationToken = default);

    /// <summary>更新雇佣阶段进度。</summary>
    Task UpdateStageProgressAsync(string hireId, string currentStage, string? testCasesStatus = null, CancellationToken cancellationToken = default);

    /// <summary>获取所有结构化数据（返回键值对字典）。</summary>
    Task<IReadOnlyDictionary<string, string?>> GetStructuredDataAsync(string hireId, CancellationToken cancellationToken = default);

    /// <summary>批量保存结构化数据。</summary>
    Task SaveStructuredDataAsync(string hireId, IReadOnlyDictionary<string, string?> data, CancellationToken cancellationToken = default);

    /// <summary>获取外部系统配置。</summary>
    Task<HiringExternalSystemConfigDto?> GetExternalConfigAsync(string hireId, CancellationToken cancellationToken = default);

    /// <summary>保存外部系统配置。</summary>
    Task SaveExternalConfigAsync(string hireId, HiringExternalSystemConfigDto config, CancellationToken cancellationToken = default);

    /// <summary>获取技能关联配置状态。</summary>
    Task<HiringSkillLinkConfigDto?> GetSkillLinkConfigAsync(string hireId, CancellationToken cancellationToken = default);

    /// <summary>保存技能关联配置状态。</summary>
    Task SaveSkillLinkConfigAsync(string hireId, HiringSkillLinkConfigDto config, CancellationToken cancellationToken = default);
}

/// <summary>
/// 雇佣阶段进度 DTO（从数据库读取）。
/// </summary>
internal sealed record HiringStageProgressDto(
    string HireId,
    string CurrentStage,
    string? PackagingTestCasesStatus,
    DateTimeOffset UpdatedAtUtc,
    string? UpdatedBy
);
