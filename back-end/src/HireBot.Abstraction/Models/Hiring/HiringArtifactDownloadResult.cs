namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringArtifactDownloadResult(
    bool Found,
    int Code,
    string Message,
    string? FileName,
    string? ContentType,
    byte[]? Content)
{
    public static HiringArtifactDownloadResult NotFound(string message) =>
        new(false, 404, message, null, null, null);

    public static HiringArtifactDownloadResult Error(int code, string message) =>
        new(false, code, message, null, null, null);

    public static HiringArtifactDownloadResult Success(string fileName, string contentType, byte[] content) =>
        new(true, 200, "下载成功", fileName, contentType, content);
}
