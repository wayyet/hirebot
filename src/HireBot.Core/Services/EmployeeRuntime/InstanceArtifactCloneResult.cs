namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例产物克隆结果。
/// </summary>
/// <param name="CurrentVersion">当前版本号</param>
/// <param name="TargetRootPath">目标根路径</param>
/// <param name="CopiedFiles">已复制的文件列表</param>
public sealed record InstanceArtifactCloneResult(
    string CurrentVersion,
    string TargetRootPath,
    IReadOnlyList<string> CopiedFiles);