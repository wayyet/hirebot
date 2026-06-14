using COSXML;
using COSXML.Auth;
using COSXML.Model.Bucket;
using COSXML.Model.Object;
using COSXML.Model.Tag;
using HireBot.Abstraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HireBot.Core.Services.Hiring.Storage;

/// <summary>
/// 基于腾讯云 COS（Cloud Object Storage）的 <see cref="IFileStore"/> 实现。
/// 虚拟路径直接映射为 COS 对象键（Object Key），支持 "/" 作为目录分隔符。
/// </summary>
public sealed class TencentCosFileStore : IFileStore
{
    private readonly CosXmlServer _cosXml;
    private readonly TencentCosOptions _options;
    private readonly ILogger<TencentCosFileStore> _logger;

    /// <summary>
    /// COS Bucket 全名：{BucketName}-{AppId}
    /// </summary>
    private string Bucket => $"{_options.BucketName}-{_options.AppId}";

    public TencentCosFileStore(
        IOptions<TencentCosOptions> options,
        ILogger<TencentCosFileStore> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new CosXmlConfig.Builder()
            .IsHttps(true)
            .SetAppid(_options.AppId)
            .SetRegion(_options.Region)
            .SetConnectionTimeoutMs(60000)
            .SetReadWriteTimeoutMs(300000)
            .Build();

        var credential = new DefaultQCloudCredentialProvider(
            _options.SecretId, _options.SecretKey, _options.KeyDurationSecond);

        _cosXml = new CosXmlServer(config, credential);
    }

    public Task<string> SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var key = NormalizeKey(path);

        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var bytes = ms.ToArray();

        var request = new PutObjectRequest(Bucket, key, bytes);
        _cosXml.PutObject(request);

        _logger.LogDebug("Uploaded to COS: {Bucket}/{Key}, Size={Size}", Bucket, key, bytes.LongLength);
        return Task.FromResult(key);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var key = NormalizeKey(path);
        var request = new GetObjectBytesRequest(Bucket, key);
        var result = _cosXml.GetObject(request);

        _logger.LogDebug("Downloaded from COS: {Bucket}/{Key}, Size={Size}", Bucket, key, result.content.Length);
        return Task.FromResult<Stream>(new MemoryStream(result.content));
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(false);
        }

        var key = NormalizeKey(path);
        var request = new DoesObjectExistRequest(Bucket, key);
        var exists = _cosXml.DoesObjectExist(request);
        return Task.FromResult(exists);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var key = NormalizeKey(path);
        var request = new DeleteObjectRequest(Bucket, key);
        _cosXml.DeleteObject(request);

        _logger.LogDebug("Deleted from COS: {Bucket}/{Key}", Bucket, key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FileStoreEntry>> ListAsync(string directoryPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPrefix);

        var prefix = NormalizeKey(directoryPrefix);
        if (!string.IsNullOrWhiteSpace(prefix) && !prefix.EndsWith('/'))
        {
            prefix += '/';
        }

        var request = new GetBucketRequest(Bucket);
        request.SetPrefix(prefix);

        var result = _cosXml.GetBucket(request);

        var listBucket = result.listBucket;
        var entries = new List<FileStoreEntry>();

        if (listBucket.contentsList is IReadOnlyList<ListBucket.Contents> contents)
        {
            foreach (var obj in contents)
            {
                if (obj is null) continue;

                var key = obj.key;
                if (string.IsNullOrWhiteSpace(key) || key == prefix) continue;

                entries.Add(new FileStoreEntry(
                    Path: key,
                    SizeBytes: obj.size));
            }
        }

        entries.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        _logger.LogDebug(
            "Listed COS bucket: {Bucket}, Prefix={Prefix}, Count={Count}",
            Bucket, prefix, entries.Count);
        return Task.FromResult<IReadOnlyList<FileStoreEntry>>(entries.AsReadOnly());
    }

    public Task<string> GetPublicUrlAsync(string path, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(path);
        var host = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? $"{Bucket}.cos.{_options.Region}.myqcloud.com"
            : _options.Endpoint.TrimEnd('/');
        return Task.FromResult($"https://{host}/{key}");
    }

    /// <summary>
    /// 规范化虚拟路径为 COS 对象键：去除首尾的 '/'，使用 '/' 作为路径分隔符。
    /// </summary>
    private static string NormalizeKey(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        return normalized.TrimStart('/');
    }
}
