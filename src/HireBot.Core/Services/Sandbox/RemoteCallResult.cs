using System.Net;

namespace HireBot.Core.Services.Sandbox;

internal sealed record RemoteCallResult<T>(bool Success, int StatusCode, string Message, T? Data)
{
    public static RemoteCallResult<T> Ok(T data, int statusCode = (int)HttpStatusCode.OK, string message = "ok")
        => new(true, statusCode, message, data);

    public static RemoteCallResult<T> Failure(int statusCode, string message)
        => new(false, statusCode, message, default);
}

internal sealed record RemoteBinaryCallResult(
    bool Success,
    int StatusCode,
    string Message,
    byte[]? Data,
    string? ContentType,
    string? FileName)
{
    public static RemoteBinaryCallResult Ok(byte[] data, string? contentType, string? fileName, int statusCode = (int)HttpStatusCode.OK)
        => new(true, statusCode, "ok", data, contentType, fileName);

    public static RemoteBinaryCallResult Failure(int statusCode, string message)
        => new(false, statusCode, message, null, null, null);
}
