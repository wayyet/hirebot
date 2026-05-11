using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Repository.Entities;

namespace HireBot.Core.Tests;

internal sealed class NoopInstanceArtifactResolver : IInstanceArtifactResolver
{
    public Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InstanceArtifactResolution(string.Empty, new Dictionary<string, string?>()));
}
