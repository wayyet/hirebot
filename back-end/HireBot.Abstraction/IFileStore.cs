namespace HireBot.Abstraction;

/// <summary>
/// 存储中的文件条目信息。
/// </summary>
public record FileStoreEntry(string Path, long SizeBytes);

/// <summary>
/// 统一的文件存储抽象。所有文件读写通过此接口进行，
/// 便于后期替换存储后端（本地文件系统、S3、MinIO、OSS 等）。
/// </summary>
public interface IFileStore
{
    /// <summary>
    /// 保存文件流到指定虚拟路径，返回存储路径（不透明 key，可用于后续 OpenRead / Exists / Delete）。
    /// </summary>
    /// <param name="path">虚拟路径，例如 "resources/todo-files/{sessionId}/{file}" 或 "artifact-store/sessions/{sessionId}/{category}/{file}"。</param>
    /// <param name="content">文件内容流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存储路径（不透明 key），可用于后续操作。</returns>
    Task<string> SaveAsync(string path, Stream content, CancellationToken cancellationToken = default);

    /// <summary>打开已存储文件的只读流。</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>判断文件是否存在。</summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>删除文件。</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出指定虚拟目录前缀下的所有文件条目（递归）。
    /// 返回的路径为相对于存储根的虚拟路径格式，包含文件大小。
    /// </summary>
    Task<IReadOnlyList<FileStoreEntry>> ListAsync(string directoryPrefix, CancellationToken cancellationToken = default);
}
