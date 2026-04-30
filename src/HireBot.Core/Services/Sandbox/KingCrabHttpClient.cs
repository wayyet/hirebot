using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Sandbox;

internal sealed class KingCrabHttpClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
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
            method,
            path,
            ownerSubject,
            useHireBotApiPrefix,
            absoluteBaseUrl,
            additionalHeaders,
            cancellationToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var content = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return RemoteCallResult<T>.Failure(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(content) ?? $"调用 KingCrab 接口失败（HTTP {(int)response.StatusCode}）");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应为空");
            }

            var payload = JsonSerializer.Deserialize<T>(content, JsonOptions);
            if (payload is null)
            {
                return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应解析为空");
            }

            return RemoteCallResult<T>.Ok(payload, (int)response.StatusCode);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "调用 KingCrab 接口被取消. Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(499, "调用已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "调用 KingCrab 接口超时. Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(504, "调用 KingCrab 接口超时");
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "调用 KingCrab 接口超时. Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(504, "调用 KingCrab 接口超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 KingCrab 接口异常. Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口异常");
        }
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
            HttpMethod.Post,
            path,
            ownerSubject,
            useHireBotApiPrefix,
            absoluteBaseUrl,
            additionalHeaders,
            cancellationToken);
        using var multipartContent = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipartContent.Add(fileContent, formFieldName, fileName);
        request.Content = multipartContent;

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var payload = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return RemoteCallResult<T>.Failure(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(payload) ?? $"调用 KingCrab 接口失败（HTTP {(int)response.StatusCode}）");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应为空");
            }

            var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            if (result is null)
            {
                return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口失败：响应解析为空");
            }

            return RemoteCallResult<T>.Ok(result, (int)response.StatusCode);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "调用 KingCrab multipart 接口被取消. Path={Path}", path);
            return RemoteCallResult<T>.Failure(499, "调用已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "调用 KingCrab multipart 接口超时. Path={Path}", path);
            return RemoteCallResult<T>.Failure(504, "调用 KingCrab 接口超时");
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "调用 KingCrab multipart 接口超时. Path={Path}", path);
            return RemoteCallResult<T>.Failure(504, "调用 KingCrab 接口超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 KingCrab multipart 接口异常. Path={Path}", path);
            return RemoteCallResult<T>.Failure(502, "调用 KingCrab 接口异常");
        }
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
            method,
            path,
            ownerSubject,
            useHireBotApiPrefix,
            absoluteBaseUrl,
            additionalHeaders,
            cancellationToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var payload = response.Content is null
                    ? null
                    : await response.Content.ReadAsStringAsync(cancellationToken);
                return RemoteBinaryCallResult.Failure(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(payload) ?? $"调用 KingCrab 接口失败（HTTP {(int)response.StatusCode}）");
            }

            var data = response.Content is null
                ? null
                : await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (data is null || data.Length == 0)
            {
                return RemoteBinaryCallResult.Failure(502, "调用 KingCrab 接口失败：响应为空");
            }

            return RemoteBinaryCallResult.Ok(
                data,
                response.Content?.Headers.ContentType?.MediaType,
                response.Content?.Headers.ContentDisposition?.FileNameStar ??
                response.Content?.Headers.ContentDisposition?.FileName);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "调用 KingCrab 二进制接口被取消. Method={Method}, Path={Path}", method, path);
            return RemoteBinaryCallResult.Failure(499, "调用已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "调用 KingCrab 二进制接口超时. Method={Method}, Path={Path}", method, path);
            return RemoteBinaryCallResult.Failure(504, "调用 KingCrab 接口超时");
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "调用 KingCrab 二进制接口超时. Method={Method}, Path={Path}", method, path);
            return RemoteBinaryCallResult.Failure(504, "调用 KingCrab 接口超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 KingCrab 二进制接口异常. Method={Method}, Path={Path}", method, path);
            return RemoteBinaryCallResult.Failure(502, "调用 KingCrab 接口异常");
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

        var requestUri = BuildRequestUri(requestPath, absoluteBaseUrl);
        var request = new HttpRequestMessage(method, requestUri);

        if (ShouldUseSandboxToken(path, absoluteBaseUrl))
        {
            var sandboxAccessToken = await sandboxTokenProvider.GetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(sandboxAccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sandboxAccessToken.Trim());
            }
        }
        else
        {
            var incomingAuthorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(incomingAuthorization))
            {
                request.Headers.TryAddWithoutValidation("Authorization", incomingAuthorization);
            }
            else
            {
                var staticToken = configuration["KingCrab:BearerToken"] ?? configuration["KingCrew:BearerToken"];
                if (!string.IsNullOrWhiteSpace(staticToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staticToken.Trim());
                }
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

    private static bool IsHttpUri(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldUseSandboxToken(string path, string? absoluteBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(absoluteBaseUrl))
        {
            return true;
        }

        return Uri.TryCreate(path, UriKind.Absolute, out var absolutePath) &&
               IsHttpUri(absolutePath);
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
}
