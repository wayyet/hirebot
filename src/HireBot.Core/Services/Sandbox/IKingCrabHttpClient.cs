namespace HireBot.Core.Services.Sandbox;

internal interface IKingCrabHttpClient
{
    Task<RemoteCallResult<T>> SendForJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken,
        bool useHireBotApiPrefix = true,
        string? absoluteBaseUrl = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null);

    Task<RemoteCallResult<T>> SendMultipartForJsonAsync<T>(
        string path,
        string formFieldName,
        string fileName,
        byte[] content,
        string contentType,
        string ownerSubject,
        CancellationToken cancellationToken,
        bool useHireBotApiPrefix = false,
        string? absoluteBaseUrl = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null);

    Task<RemoteBinaryCallResult> SendForBinaryAsync(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken,
        bool useHireBotApiPrefix = true,
        string? absoluteBaseUrl = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null);
}
