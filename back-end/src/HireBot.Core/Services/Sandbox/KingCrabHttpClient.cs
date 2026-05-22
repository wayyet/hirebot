using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Sandbox;

internal sealed class KingCrabHttpClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    KingCrabSandboxTokenProvider sandboxTokenProvider,
    ILogger<KingCrabHttpClient> logger) : IKingCrabHttpClient
{
    private const string ClientName = "KingCrab";
    private const string DefaultHireBotApiPrefix = "/api/integration/hirebot";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<RemoteCallResult<T>> SendForJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken,
        bool useHireBotApiPrefix = true,
        string? absoluteBaseUrl = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        if (client.BaseAddress is null && string.IsNullOrWhiteSpace(absoluteBaseUrl))
        {
            return RemoteCallResult<T>.Failure(500, "KingCrab:BaseUrl 未配置");
        }

        using var request = await BuildRequestAsync(
            method, path, ownerSubject, useHireBotApiPrefix, absoluteBaseUrl, additionalHeaders, cancellationToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await SendAsync(client, request, cancellationToken, $"KingCrab 接口", path, method);
        if (!response.Success)
        {
            return RemoteCallResult<T>.Failure(response.StatusCode, response.Message);
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应为空");
        }

        logger.LogDebug(
            "KingCrab JSON response body. Method={Method}, Path={Path}, ContentType={ContentType}, BodyLength={BodyLength}, BodyPreview={BodyPreview}",
            method,
            path,
            typeof(T).Name,
            response.Content.Length,
            response.Content.Length <= 2000 ? response.Content : response.Content[..2000] + "...");

        var payload = JsonSerializer.Deserialize<T>(response.Content, JsonOptions);
        if (payload is null)
        {
            logger.LogWarning(
                "KingCrab JSON deserialization returned null. Method={Method}, Path={Path}, ContentType={ContentType}, BodyLength={BodyLength}, BodyPreview={BodyPreview}",
                method,
                path,
                typeof(T).Name,
                response.Content.Length,
                response.Content.Length <= 500 ? response.Content : response.Content[..500] + "...");
            return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应解析为空");
        }

        return RemoteCallResult<T>.Ok(payload, response.StatusCode);
    }

    public async Task<RemoteCallResult<T>> SendMultipartForJsonAsync<T>(
        string path,
        string formFieldName,
        string fileName,
        byte[] content,
        string contentType,
        string ownerSubject,
        CancellationToken cancellationToken,
        bool useHireBotApiPrefix = false,
        string? absoluteBaseUrl = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        if (client.BaseAddress is null && string.IsNullOrWhiteSpace(absoluteBaseUrl))
        {
            return RemoteCallResult<T>.Failure(500, "KingCrab:BaseUrl 未配置");
        }

        using var request = await BuildRequestAsync(
            HttpMethod.Post, path, ownerSubject, useHireBotApiPrefix, absoluteBaseUrl, additionalHeaders, cancellationToken);
        using var multipartContent = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipartContent.Add(fileContent, formFieldName, fileName);
        request.Content = multipartContent;

        var response = await SendAsync(client, request, cancellationToken, $"KingCrab multipart 接口", path);
        if (!response.Success)
        {
            return RemoteCallResult<T>.Failure(response.StatusCode, response.Message);
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应为空");
        }

        var result = JsonSerializer.Deserialize<T>(response.Content, JsonOptions);
        if (result is null)
        {
            return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应解析为空");
        }

        return RemoteCallResult<T>.Ok(result, response.StatusCode);
    }

    public async Task<RemoteBinaryCallResult> SendForBinaryAsync(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken,
        bool useHireBotApiPrefix = true,
        string? absoluteBaseUrl = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null)
    {
        var client = httpClientFactory.CreateClient(ClientName);
        if (client.BaseAddress is null && string.IsNullOrWhiteSpace(absoluteBaseUrl))
        {
            return RemoteBinaryCallResult.Failure(500, "KingCrab:BaseUrl 未配置");
        }

        using var request = await BuildRequestAsync(
            method, path, ownerSubject, useHireBotApiPrefix, absoluteBaseUrl, additionalHeaders, cancellationToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await SendBinaryAsync(client, request, cancellationToken, $"KingCrab 二进制接口", path, method);
        if (!response.Success)
        {
            return RemoteBinaryCallResult.Failure(response.StatusCode, response.Message);
        }

        if (response.Data is null || response.Data.Length == 0)
        {
            return RemoteBinaryCallResult.Failure(502, "调用 KingCrab 接口失败：响应为空");
        }

        return RemoteBinaryCallResult.Ok(
            response.Data,
            response.ContentType,
            response.FileName);
    }

    private async Task<HttpResponseWrapper> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        string endpointDescription,
        string path,
        HttpMethod? method = null)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var content = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = ExtractRemoteMessage(content) ?? $"调用 {endpointDescription} 失败（HTTP {(int)response.StatusCode}）";
                return HttpResponseWrapper.Failure((int)response.StatusCode, message);
            }

            return HttpResponseWrapper.Ok(content, (int)response.StatusCode);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "调用 {EndpointDescription} 被取消. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpResponseWrapper.Failure(499, "调用已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "调用 {EndpointDescription} 超时. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpResponseWrapper.Failure(504, $"调用 {endpointDescription} 超时");
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "调用 {EndpointDescription} 超时. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpResponseWrapper.Failure(504, $"调用 {endpointDescription} 超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 {EndpointDescription} 异常. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpResponseWrapper.Failure(502, $"调用 {endpointDescription} 异常");
        }
    }

    private async Task<HttpBinaryResponseWrapper> SendBinaryAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        string endpointDescription,
        string path,
        HttpMethod? method = null)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var payload = response.Content is null
                    ? null
                    : await response.Content.ReadAsStringAsync(cancellationToken);
                var message = ExtractRemoteMessage(payload) ?? $"调用 {endpointDescription} 失败（HTTP {(int)response.StatusCode}）";
                return HttpBinaryResponseWrapper.Failure((int)response.StatusCode, message);
            }

            var data = response.Content is null
                ? null
                : await response.Content.ReadAsByteArrayAsync(cancellationToken);

            return HttpBinaryResponseWrapper.Ok(
                data,
                response.Content?.Headers.ContentType?.MediaType,
                response.Content?.Headers.ContentDisposition?.FileNameStar ??
                response.Content?.Headers.ContentDisposition?.FileName,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "调用 {EndpointDescription} 被取消. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpBinaryResponseWrapper.Failure(499, "调用已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "调用 {EndpointDescription} 超时. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpBinaryResponseWrapper.Failure(504, $"调用 {endpointDescription} 超时");
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "调用 {EndpointDescription} 超时. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpBinaryResponseWrapper.Failure(504, $"调用 {endpointDescription} 超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 {EndpointDescription} 异常. Method={Method}, Path={Path}", endpointDescription, method, path);
            return HttpBinaryResponseWrapper.Failure(502, $"调用 {endpointDescription} 异常");
        }
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method,
        string path,
        string ownerSubject,
        bool useHireBotApiPrefix,
        string? absoluteBaseUrl,
        IReadOnlyDictionary<string, string>? additionalHeaders,
        CancellationToken cancellationToken)
    {
        var requestPath = BuildRequestPath(path, useHireBotApiPrefix);
        // 按配置的 OpenSandbox:Protocol 规范化网关地址的协议，
        // 避免 DB 中存储的旧 http:// 地址在 https-only gateway 上跟随重定向后丢失 Authorization header 导致 401。
        var normalizedBaseUrl = NormalizeGatewayScheme(absoluteBaseUrl);
        var requestUri = BuildRequestUri(requestPath, normalizedBaseUrl);
        var request = new HttpRequestMessage(method, requestUri);

        var serviceToken = await sandboxTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(serviceToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken.Trim());
        }
        else
        {
            var staticToken = configuration["KingCrab:BearerToken"] ?? configuration["KingCrew:BearerToken"];
            if (!string.IsNullOrWhiteSpace(staticToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staticToken.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(ownerSubject))
        {
            request.Headers.TryAddWithoutValidation("X-HireBot-Owner", ownerSubject);
        }

        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key.Trim(), header.Value.Trim());
            }
        }

        return request;
    }

    private string BuildRequestPath(string path, bool useHireBotApiPrefix)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolutePath) &&
            IsHttpUri(absolutePath))
        {
            return absolutePath.ToString();
        }

        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return useHireBotApiPrefix
            ? $"{ResolveHireBotApiPrefix()}{normalizedPath}"
            : normalizedPath;
    }

    private string ResolveHireBotApiPrefix()
    {
        var prefix = configuration["KingCrab:HireBotApiPrefix"] ?? configuration["KingCrew:HireBotApiPrefix"];
        return string.IsNullOrWhiteSpace(prefix)
            ? DefaultHireBotApiPrefix
            : "/" + prefix.Trim().Trim('/');
    }

    private static Uri BuildRequestUri(string requestPath, string? absoluteBaseUrl)
    {
        if (Uri.TryCreate(requestPath, UriKind.Absolute, out var absoluteRequest) &&
            IsHttpUri(absoluteRequest))
        {
            return absoluteRequest;
        }

        if (!string.IsNullOrWhiteSpace(absoluteBaseUrl))
        {
            var normalizedBaseUrl = absoluteBaseUrl.Trim();
            if (!StartsWithHttpScheme(normalizedBaseUrl))
            {
                normalizedBaseUrl = $"http://{normalizedBaseUrl.TrimStart('/')}";
            }

            var baseUri = new Uri(normalizedBaseUrl, UriKind.Absolute);
            if (!IsHttpUri(baseUri))
            {
                throw new InvalidOperationException($"Unsupported gateway endpoint scheme: {baseUri.Scheme}");
            }

            if (!normalizedBaseUrl.EndsWith("/", StringComparison.Ordinal))
            {
                baseUri = new Uri(normalizedBaseUrl + "/", UriKind.Absolute);
            }

            return new Uri(baseUri, requestPath.TrimStart('/'));
        }

        return new Uri(requestPath, UriKind.Relative);
    }

    private static bool StartsWithHttpScheme(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 当 OpenSandbox:Protocol 配置为 Https 时，将网关地址的 http:// 升级为 https://，
    /// 无 scheme 的地址也直接补 https://。防止 http→https 重定向导致 Authorization header 被 strip。
    /// </summary>
    private string? NormalizeGatewayScheme(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        var protocol = configuration["OpenSandbox:Protocol"];
        var forceHttps = string.Equals(protocol, "Https", StringComparison.OrdinalIgnoreCase);
        if (!forceHttps) return url;

        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return string.Concat("https://", trimmed.AsSpan("http://".Length));

        // 无 scheme：直接补 https://
        if (!StartsWithHttpScheme(trimmed))
            return $"https://{trimmed.TrimStart('/')}";

        return trimmed;
    }

    private static bool IsHttpUri(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractRemoteMessage(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString();
            }
        }
        catch
        {
            // Ignore parse failures and fall back to generic message.
        }

        return null;
    }

    private sealed record HttpResponseWrapper(bool Success, int StatusCode, string? Message, string? Content)
    {
        public static HttpResponseWrapper Ok(string? content, int statusCode) => new(true, statusCode, null, content);
        public static HttpResponseWrapper Failure(int statusCode, string message) => new(false, statusCode, message, null);
    }

    private sealed record HttpBinaryResponseWrapper(bool Success, int StatusCode, string? Message, byte[]? Data, string? ContentType, string? FileName)
    {
        public static HttpBinaryResponseWrapper Ok(byte[]? data, string? contentType, string? fileName, int statusCode) => new(true, statusCode, null, data, contentType, fileName);
        public static HttpBinaryResponseWrapper Failure(int statusCode, string message) => new(false, statusCode, message, null, null, null);
    }
}
