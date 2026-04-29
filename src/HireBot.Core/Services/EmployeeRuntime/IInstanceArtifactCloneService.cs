using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Core.Services.EmployeeRuntime;

public interface IInstanceArtifactCloneService
{
    Task<InstanceArtifactCloneResult> CloneArtifactsAsync(
        EmployeeDetailDto source,
        string targetInstanceId,
        CancellationToken cancellationToken = default);

    Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(
        string departmentInstanceId,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken cancellationToken = default);
}
