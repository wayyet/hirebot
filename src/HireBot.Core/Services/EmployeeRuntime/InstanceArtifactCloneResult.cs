namespace HireBot.Core.Services.EmployeeRuntime;

public sealed record InstanceArtifactCloneResult(
    string CurrentVersion,
    string TargetRootPath,
    IReadOnlyList<string> CopiedFiles);
