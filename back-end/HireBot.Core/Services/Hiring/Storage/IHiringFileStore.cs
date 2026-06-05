namespace HireBot.Core.Services.Hiring.Storage;

public interface IHiringFileStore
{
    Task<string> SaveAsync(
        string sessionId,
        string category,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按语义路径保存最终候选包：{root}/{tenantId}/{hireId}/{packageId}/package.zip。
    /// 路径自描述，无需借助 HiringArtifacts 表即可还原。
    /// </summary>
    Task<string> SaveFinalPackageAsync(
        string tenantId,
        string hireId,
        string packageId,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按语义坐标直接打开最终候选包流。
    /// </summary>
    Task<Stream> OpenFinalPackageAsync(
        string tenantId,
        string hireId,
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断语义路径的最终候选包文件是否存在。
    /// </summary>
    Task<bool> FinalPackageExistsAsync(
        string tenantId,
        string hireId,
        string packageId,
        CancellationToken cancellationToken = default);
}

