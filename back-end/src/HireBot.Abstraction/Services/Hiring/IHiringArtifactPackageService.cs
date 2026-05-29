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
