using HireBot.Abstraction;

namespace HireBot.Core.Services.Hiring.Storage;

/// <summary>
/// 基于本地文件系统的 <see cref="IFileStore"/> 实现。
/// 虚拟路径映射到 <c>{rootPath}/{virtualPath}</c>。
/// 支持向后兼容：如果传入的 path 已经是绝对路径，则直接使用。
/// </summary>
public sealed class FileSystemFileStore : IFileStore
{
    private readonly string _rootPath;

    public FileSystemFileStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task<string> SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = ResolveFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        // 安全写入：先写到临时文件，再 rename（避免写入过程中崩溃导致文件损坏）
        var tempPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
            {
                await content.CopyToAsync(target, cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            throw;
        }

        return fullPath;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = ResolveFullPath(path);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(false);
        }

        var fullPath = ResolveFullPath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = ResolveFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FileStoreEntry>> ListAsync(string directoryPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPrefix);

        var fullPath = ResolveFullPath(directoryPrefix);
        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult<IReadOnlyList<FileStoreEntry>>(Array.Empty<FileStoreEntry>());
        }

        var entries = new DirectoryInfo(fullPath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith(".tmp.", StringComparison.Ordinal)) // 过滤掉临时文件
            .Select(f => new FileStoreEntry(
                Path: Path.GetRelativePath(_rootPath, f.FullName).Replace('\\', '/'),
                SizeBytes: f.Length))
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<FileStoreEntry>>(entries.AsReadOnly());
    }

    public Task<string> GetPublicUrlAsync(string path, CancellationToken cancellationToken = default)
    {
        // 本地文件系统返回 web 相对路径，由 PhysicalFileProvider 映射提供访问
        var normalized = path.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("artifact-store/", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult($"/resources/{normalized}");
        }

        var resourcesIndex = normalized.IndexOf("/resources/", StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex > 0)
        {
            return Task.FromResult($"/resources/{normalized}");
        }

        return Task.FromResult($"/{normalized}");
    }

    /// <summary>
    /// 解析虚拟路径→绝对路径。
    /// 如果 path 已经是绝对路径，直接使用（向后兼容已有的数据库记录）；
    /// 否则相对于 <see cref="_rootPath"/> 拼接。
    /// </summary>
    private string ResolveFullPath(string path)
    {
        var normalized = path.Trim().Replace('/', Path.DirectorySeparatorChar);

        string fullPath;
        if (Path.IsPathRooted(normalized))
        {
            fullPath = Path.GetFullPath(normalized);
        }
        else
        {
            fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        }

        // 防御路径遍历攻击：确保解析后的路径仍在 _rootPath 范围内
        if (!IsPathUnderRoot(fullPath, _rootPath))
        {
            throw new InvalidOperationException($"Path traversal detected: '{path}' resolves outside the storage root.");
        }

        return fullPath;
    }

    private static bool IsPathUnderRoot(string candidatePath, string rootDirectory)
    {
        var normalizedRoot = rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? rootDirectory
            : rootDirectory + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
