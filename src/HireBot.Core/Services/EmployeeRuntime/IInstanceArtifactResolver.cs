using HireBot.Repository.Entities;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例产物解析器接口，负责解析实例的产物路径和元数据。
/// </summary>
public interface IInstanceArtifactResolver
{
    /// <summary>
    /// 解析实例的产物路径和元数据。
    /// </summary>
    /// <param name="instance">实例实体</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>产物解析结果</returns>
    Task<InstanceArtifactResolution> ResolveAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 实例产物解析结果。
/// </summary>
/// <param name="ArtifactRoot">产物根目录路径</param>
/// <param name="Metadata">元数据字典</param>
public sealed record InstanceArtifactResolution(
    string ArtifactRoot,
    IReadOnlyDictionary<string, string?> Metadata);