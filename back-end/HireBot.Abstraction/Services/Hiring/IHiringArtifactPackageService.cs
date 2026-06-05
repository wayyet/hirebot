using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Abstraction.Services.Hiring;

public interface IHiringArtifactPackageService
{
    Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(
        HiringArtifactPackagePersistRequestDto request,
        CancellationToken cancellationToken = default);

    Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(
        HiringArtifactPackagePersistRequestDto request,
        CancellationToken cancellationToken = default);

    Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(
        string hireId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过员工实例 ID 反查关联的最终产物包。
    /// 导入时在结构化数据中写入 linked_employee_id 字段，此处做反向查找。
    /// </summary>
    Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageByEmployeeIdAsync(
        string employeeId,
        CancellationToken cancellationToken = default);

    Task<HiringArtifactPackageSnapshotDto?> GetPackageByKindAsync(
        string hireId,
        string kind,
        CancellationToken cancellationToken = default);

    Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(
        string hireId,
        CancellationToken cancellationToken = default);

    Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(
        string hireId,
        string artifactName,
        CancellationToken cancellationToken = default);
}
