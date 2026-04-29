namespace HireBot.Core.Services.Sandbox;

internal sealed class KingCrabGatewayClient(IKingCrabHttpClient kingCrabHttpClient)
{
    public Task<RemoteCallResult<MediaUploadResult>> UploadMediaAsync(
        string ownerSubject,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken,
        string? absoluteBaseUrl = null)
    {
        return UploadInternalAsync(ownerSubject, fileName, content, contentType, cancellationToken, absoluteBaseUrl);
    }

    private async Task<RemoteCallResult<MediaUploadResult>> UploadInternalAsync(
        string ownerSubject,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken,
        string? absoluteBaseUrl)
    {
        var uploadCall = await kingCrabHttpClient.SendMultipartForJsonAsync<MediaUploadResponse>(
            "/media/upload",
            "file",
            fileName,
            content,
            contentType,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: absoluteBaseUrl);

        if (!uploadCall.Success || uploadCall.Data is null)
        {
            return RemoteCallResult<MediaUploadResult>.Failure(uploadCall.StatusCode, uploadCall.Message);
        }

        var payload = uploadCall.Data;
        return RemoteCallResult<MediaUploadResult>.Ok(new MediaUploadResult(
            payload.Id,
            payload.Url,
            payload.FileName,
            payload.MimeType,
            payload.SizeBytes,
            $"[FILE_URL:/media/{payload.Id}]"));
    }

    internal sealed record MediaUploadResult(
        string MediaId,
        string Url,
        string FileName,
        string MimeType,
        long SizeBytes,
        string Marker);

    private sealed record MediaUploadResponse(
        string Id,
        string Url,
        string FileName,
        string MimeType,
        long SizeBytes);
}
