namespace HireBot.Core.Services.Hiring.StoreSkills;

internal sealed record StoreSkillDownloadRequest(
    string SkillId,
    string? VersionId = null,
    string? PreferredSlug = null);

/// <summary>
/// 在导入最终实例包前，根据技能关联配置从 store 下载技能包，并规范化为 skills/&lt;slug&gt;/... 路径布局。
/// </summary>
internal interface IStoreSkillPackageDownloader
{
    /// <summary>
    /// 下载并解压 store skill 包，返回可直接合并到实例包的相对路径文件字典。
    /// </summary>
    Task<IReadOnlyDictionary<string, byte[]>> DownloadSkillsAsync(
        IReadOnlyList<StoreSkillDownloadRequest> skillRequests,
        CancellationToken cancellationToken = default);
}
