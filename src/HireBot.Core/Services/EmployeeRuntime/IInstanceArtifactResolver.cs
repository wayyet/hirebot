using HireBot.Repository.Entities;

namespace HireBot.Core.Services.EmployeeRuntime;

public interface IInstanceArtifactResolver
{
    Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken = default);
}

public sealed record InstanceArtifactResolution(
    string ArtifactRoot,
    IReadOnlyDictionary<string, string?> Metadata);

