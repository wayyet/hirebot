namespace HireBot.Core.Services.Internal;

/// <summary>
/// 统一的存储路径构建器。所有路径均以 {ProjectRoot}/{tenantId} 做租户隔离前缀。
/// 统一存储为单个 .zip 文件，不再使用散文件目录。
/// </summary>
public static class ArtifactStoragePaths
{
    public const string DefaultProjectRoot = "goodcrew-hire";
    private static string _projectRoot = DefaultProjectRoot;

    public static string ProjectRoot => _projectRoot;

    public static void ConfigureProjectRoot(string? projectRoot)
    {
        _projectRoot = Sanitize(projectRoot ?? DefaultProjectRoot);
    }

    /// <summary>雇佣最终包: {ProjectRoot}/{tenant}/hirings/{hireId}/{hireId}-{packageId前8}.zip</summary>
    public static string BuildFinalPackagePath(string tenantId, string hireId, string packageId)
    {
        var shortId = Sanitize(packageId).Length > 8
            ? Sanitize(packageId)[..8]
            : Sanitize(packageId);
        return $"{ProjectRoot}/{Sanitize(tenantId)}/hirings/{Sanitize(hireId)}/{Sanitize(hireId)}-{shortId}.zip";
    }

    /// <summary>雇佣中间包: {ProjectRoot}/{tenant}/sessions/{sessionId}/{category}/package.zip</summary>
    public static string BuildIntermediatePackagePath(string tenantId, string sessionId, string category)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/sessions/{Sanitize(sessionId)}/{Sanitize(category)}/package.zip";

    /// <summary>部门员工版本包: {ProjectRoot}/{tenant}/instances/department/{id}/versions/{v}.zip</summary>
    public static string BuildDepartmentVersionPath(string tenantId, string employeeId, string version)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/instances/department/{Sanitize(employeeId)}/versions/{Sanitize(version)}.zip";

    /// <summary>个人分身版本包: {ProjectRoot}/{tenant}/instances/personal_clone/{fromId}/{id}/versions/{v}.zip</summary>
    public static string BuildPersonalCloneVersionPath(string tenantId, string fromInstanceId, string cloneId, string version)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/instances/personal_clone/{Sanitize(fromInstanceId)}/{Sanitize(cloneId)}/versions/{Sanitize(version)}.zip";

    /// <summary>个人分身产物（备用路径）: {ProjectRoot}/{tenant}/personal-clone-artifacts/{srcId}/{cloneId}/versions/{v}.zip</summary>
    public static string BuildPersonalCloneArtifactsPath(string tenantId, string srcId, string cloneId, string version)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/personal-clone-artifacts/{Sanitize(srcId)}/{Sanitize(cloneId)}/versions/{Sanitize(version)}.zip";

    /// <summary>快速创建/旧版产物: {ProjectRoot}/{tenant}/digital-workforce/{employeeId}.zip</summary>
    public static string BuildDigitalWorkforcePath(string tenantId, string employeeId)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/digital-workforce/{Sanitize(employeeId)}.zip";

    /// <summary>私有分支快照: {ProjectRoot}/{tenant}/instances/personal_clone/{parentId}/{id}/snapshots/pre_private_branch.zip</summary>
    public static string BuildSnapshotPath(string tenantId, string parentInstanceId, string instanceId)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/instances/personal_clone/{Sanitize(parentInstanceId)}/{Sanitize(instanceId)}/snapshots/pre_private_branch.zip";

    /// <summary>评估资源: {ProjectRoot}/{tenant}/resources/evaluation/{sessionId}/...</summary>
    public static string BuildEvaluationResourcePath(string tenantId, string sessionId, int iteration, string assetType, string fileName)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/resources/evaluation/{Sanitize(sessionId)}/iter-{Math.Max(1, iteration):D2}/{Sanitize(assetType)}/{Sanitize(fileName)}";

    /// <summary>雇佣资料文件: {ProjectRoot}/{tenant}/resources/todo-files/{sessionId}/{relativePath}</summary>
    public static string BuildTodoFilePath(string tenantId, string sessionId, string relativePath)
        => $"{ProjectRoot}/{Sanitize(tenantId)}/resources/todo-files/{Sanitize(sessionId)}/{SanitizeRelativePath(relativePath)}";

    public static string SanitizeRelativePath(string value)
    {
        var segments = value.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Sanitize)
            .Where(segment => segment.Length > 0)
            .ToArray();

        return segments.Length == 0 ? "unknown" : string.Join('/', segments);
    }

    /// <summary>从路径中提取可下载的文件名</summary>
    public static string ExtractDownloadFileName(string path)
    {
        var segments = path.TrimEnd('/').Split('/');
        return segments.Length > 0 ? segments[^1] : path;
    }

    /// <summary>清理路径段中的非法字符</summary>
    public static string Sanitize(string value)
    {
        var trimmed = value.Trim();
        var chars = trimmed.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }
}
