namespace HireBot.Core.Services.Hiring.Storage;

/// <summary>存储配置根节点</summary>
public class StorageSettings
{
    public const string SectionName = "Storage";

    /// <summary>存储提供程序，可选值见 <see cref="StorageProvider"/></summary>
    public string Provider { get; set; } = StorageProvider.FileSystem;

    public TencentCosOptions TencentCos { get; set; } = new();
    public AliyunOssOptions AliyunOss { get; set; } = new();
    public MinioOptions Minio { get; set; } = new();
}

/// <summary>存储提供程序常量</summary>
public static class StorageProvider
{
    public const string FileSystem = "FileSystem";
    public const string TencentCos = "TencentCos";
    public const string AliyunOss = "AliyunOss";
    public const string MinIO = "MinIO";
}

public class TencentCosOptions
{
    public string AppId { get; set; } = string.Empty;
    public string SecretId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public int KeyDurationSecond { get; set; } = 600;

    /// <summary>自定义访问域名，为空则自动拼接 {BucketName}-{AppId}.cos.{Region}.myqcloud.com</summary>
    public string? Endpoint { get; set; }
}

public class AliyunOssOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string AccessKeySecret { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
}

public class MinioOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
}
