namespace HireBot.Core.Services.Sandbox;

internal sealed class KingCrabGatewayClient(IKingCrabHttpClient kingCrabHttpClient)
{
    public async Task<RemoteCallResult<MediaUploadResult>> UploadMediaAsync(
        string ownerSubject,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken,
        string? absoluteBaseUrl = null)
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
        var resolvedUrl = ResolveMediaUrl(absoluteBaseUrl, payload.Url, payload.Id);
        return RemoteCallResult<MediaUploadResult>.Ok(new MediaUploadResult(
            payload.Id,
            resolvedUrl,
            payload.FileName,
            payload.MimeType,
            payload.SizeBytes,
            $"[FILE_URL:/app/memory/media-cache/{payload.Id}]"));
    }

    private static string ResolveMediaUrl(string? absoluteBaseUrl, string? payloadUrl, string mediaId)
    {
        var fallbackPath = $"/app/memory/media-cache/{mediaId}";
        var candidateUrl = string.IsNullOrWhiteSpace(payloadUrl) ? fallbackPath : payloadUrl.Trim();
        if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        if (!string.IsNullOrWhiteSpace(absoluteBaseUrl) &&
            Uri.TryCreate(absoluteBaseUrl.Trim(), UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, candidateUrl, out var resolvedUri))
        {
            return resolvedUri.ToString();
        }

        return candidateUrl;
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

    public async Task<RemoteCallResult<WorkspaceUploadResult>> UploadToWorkspaceAsync(
        string ownerSubject,
        string fileName,
        byte[] content,
        string contentType,
        string targetDir,
        CancellationToken cancellationToken,
        string? absoluteBaseUrl = null)
    {
        var encodedDir = Uri.EscapeDataString(targetDir.Trim('/'));
        var path = $"/admin/workspace/upload?dir={encodedDir}";

        var uploadCall = await kingCrabHttpClient.SendMultipartForJsonAsync<WorkspaceUploadResponse>(
            path,
            "file",
            fileName,
            content,
            contentType,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: absoluteBaseUrl);

        if (!uploadCall.Success || uploadCall.Data is null)
            return RemoteCallResult<WorkspaceUploadResult>.Failure(uploadCall.StatusCode, uploadCall.Message);

        var payload = uploadCall.Data;
        if (!payload.Success)
            return RemoteCallResult<WorkspaceUploadResult>.Failure(422, payload.Error ?? "workspace upload failed");

        return RemoteCallResult<WorkspaceUploadResult>.Ok(
            new WorkspaceUploadResult(payload.Files ?? [], payload.FileCount));
    }

    internal sealed record WorkspaceUploadResult(
        IReadOnlyList<string> Files,
        int FileCount);

    private sealed record WorkspaceUploadResponse(
        bool Success,
        List<string>? Files,
        int FileCount,
        string? Error);
}
