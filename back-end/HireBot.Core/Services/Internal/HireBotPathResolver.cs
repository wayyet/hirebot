namespace HireBot.Core.Services.Internal;

public static class HireBotPathResolver
{
    public const string DefaultDataRoot = "ncrew-hire-data";
    public const string DefaultEvaluationResourcesSubdir = "resources";
    public const string DefaultEvaluationSubdir = "evaluation";
    public const string DefaultDigitalWorkforceSubdir = "digital-workforce";
    public const string DefaultPersonalCloneArtifactsSubdir = "personal-clone-artifacts";
    public const string DefaultInstanceFixturesSubdir = "instance-fixtures";
    public const string DefaultTodoFilesSubdir = "todo-files";
    public const string DefaultTemplatePackageCacheSubdir = "template-package-cache";
    public const string DefaultArtifactStoreSubdir = "artifact-store";

    public static string ResolveDataRoot(string contentRootPath, string? configuredDataRoot)
    {
        var effectiveRoot = string.IsNullOrWhiteSpace(configuredDataRoot)
            ? DefaultDataRoot
            : configuredDataRoot.Trim();

        return ResolvePath(contentRootPath, effectiveRoot);
    }

    public static string ResolveEvaluationResourceRoot(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredResourceRoot)
    {
        return ResolveDataScopedPath(
            contentRootPath,
            configuredDataRoot,
            configuredResourceRoot,
            DefaultEvaluationResourcesSubdir);
    }

    public static string ResolveDigitalWorkforceRoot(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredDigitalWorkforceRoot)
    {
        return ResolveDataScopedPath(
            contentRootPath,
            configuredDataRoot,
            configuredDigitalWorkforceRoot,
            DefaultDigitalWorkforceSubdir);
    }

    public static string ResolvePersonalCloneArtifactsRoot(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredPersonalCloneArtifactsRoot)
    {
        return ResolveDataScopedPath(
            contentRootPath,
            configuredDataRoot,
            configuredPersonalCloneArtifactsRoot,
            DefaultPersonalCloneArtifactsSubdir);
    }

    public static string ResolveArtifactStoreRoot(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredArtifactStoreRoot)
    {
        return ResolveDataScopedPath(
            contentRootPath,
            configuredDataRoot,
            configuredArtifactStoreRoot,
            DefaultArtifactStoreSubdir);
    }

    public static string ResolveInstanceFixturesRoot(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredInstanceFixturesRoot)
    {
        return ResolveDataScopedPath(
            contentRootPath,
            configuredDataRoot,
            configuredInstanceFixturesRoot,
            DefaultInstanceFixturesSubdir);
    }

    public static string ResolveTodoFilesRoot(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredResourceRoot)
    {
        return Path.Combine(
            ResolveEvaluationResourceRoot(contentRootPath, configuredDataRoot, configuredResourceRoot),
            DefaultTodoFilesSubdir);
    }

    public static string ResolveEvaluationTemplatePackageCacheRoot(
        string contentRootPath,
        string? configuredDataRoot)
    {
        return Path.Combine(
            ResolveDataRoot(contentRootPath, configuredDataRoot),
            DefaultEvaluationSubdir,
            DefaultTemplatePackageCacheSubdir);
    }

    public static string? ResolveConventionalInstanceFixturesRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), DefaultDataRoot, DefaultInstanceFixturesSubdir),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "HireBot.ApiService", DefaultDataRoot, DefaultInstanceFixturesSubdir),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", DefaultDataRoot, DefaultInstanceFixturesSubdir)),
            Path.Combine(AppContext.BaseDirectory, DefaultDataRoot, DefaultInstanceFixturesSubdir)
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string ResolveDataScopedPath(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredPath,
        string defaultSubdirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(
                ResolveDataRoot(contentRootPath, configuredDataRoot),
                defaultSubdirectory);
        }

        var effectivePath = configuredPath.Trim();
        if (Path.IsPathRooted(effectivePath))
        {
            return Path.GetFullPath(effectivePath);
        }

        return Path.GetFullPath(Path.Combine(
            ResolveDataRoot(contentRootPath, configuredDataRoot),
            effectivePath));
    }

    private static string ResolvePath(string contentRootPath, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(contentRootPath, path));
    }

    /// <summary>
    /// 判断候选路径是否在指定根目录下（防御性路径遍历校验）。
    /// </summary>
    public static bool IsPathUnderRoot(string candidatePath, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootDirectory))
        {
            return false;
        }

        string normalizedCandidate;
        string normalizedRoot;
        try
        {
            normalizedCandidate = Path.GetFullPath(candidatePath.Trim());
            normalizedRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(rootDirectory.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
