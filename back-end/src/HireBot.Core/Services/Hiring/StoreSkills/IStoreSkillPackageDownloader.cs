namespace HireBot.Core.Services.Hiring.StoreSkills;

/// <summary>
/// 雇佣流程"导入实例包"前的扩展：根据用户在前端 TODO 面板关联的 store skill UUID，
/// 从 ncrew-builder（BuildService）下载技能包 zip，解析为 skills/&lt;slug&gt;/... 相对路径文件。
/// </summary>
internal interface IStoreSkillPackageDownloader
{
    /// <summary>
    /// 拉取并解压 store skill 包，返回相对路径到字节内容的字典。所有路径已规范化为
    /// <c>skills/&lt;skill-slug&gt;/...</c> 形式，可直接合并到产物字典中。
    /// </summary>
    /// <param name="skillIds">用户关联的 store skill UUID 列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<string, byte[]>> DownloadSkillsAsync(
        IReadOnlyList<string> skillIds,
        CancellationToken cancellationToken = default);
}
