using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例产物克隆服务接口，负责克隆和存储员工实例的产物文件。
/// </summary>
public interface IInstanceArtifactCloneService
{
    /// <summary>
    /// 克隆源员工的产物到目标实例。
    /// </summary>
    /// <param name="source">源员工详情</param>
    /// <param name="targetInstanceId">目标实例ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>克隆结果</returns>
    Task<InstanceArtifactCloneResult> CloneArtifactsAsync(
        EmployeeDetailDto source,
        string targetInstanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 存储部门员工的产物文件。
    /// </summary>
    /// <param name="departmentInstanceId">部门实例ID</param>
    /// <param name="files">文件内容字典（文件名 -> 字节数组）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存储结果</returns>
    Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(
        string departmentInstanceId,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken cancellationToken = default);
}