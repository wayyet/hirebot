using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using HireBot.Abstraction.Models.Hiring;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal interface IHiringMcpConnectivityTester
{
    Task<HiringMcpConnectivityTestResultDto> TestAsync(
        HiringMcpConnectivityTestRequestDto request,
        CancellationToken cancellationToken = default);
}

internal sealed class HiringMcpConnectivityTester(
    IHttpClientFactory httpClientFactory,
    ILogger<HiringMcpConnectivityTester> logger) : IHiringMcpConnectivityTester
{
    internal const string HttpClientName = "HiringMcpConnectivity";

    private static readonly HashSet<string> SupportedTransports =
    [
        "sse",
        "streamable-http",
        "http"
    ];

    public async Task<HiringMcpConnectivityTestResultDto> TestAsync(
        HiringMcpConnectivityTestRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.Server is null)
        {
            return BuildFailure("invalid_config", "MCP 服务配置不能为空。");
        }

        var server = request.Server;
        var transport = NormalizeTransport(server.Transport);
        if (!SupportedTransports.Contains(transport))
        {
            return BuildFailure("unsupported_transport", $"暂不支持 {server.Transport} 传输方式的连通性测试。", transport);
        }

        if (!TryCreateEndpoint(server.Url, out var endpoint))
        {
            return BuildFailure("invalid_url", "MCP 服务 URL 必须是有效的 http 或 https 绝对地址。", transport);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var headerError = ApplyHeaders(server, httpRequest);
        if (headerError is not null)
        {
            return BuildFailure(headerError.Value.Status, headerError.Value.Message, transport);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            stopwatch.Stop();
            return BuildHttpResult(response.StatusCode, stopwatch.ElapsedMilliseconds, transport);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return BuildFailure("timeout", "MCP 服务连接超时。", transport, stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogWarning(
                ex,
                "MCP 服务连通性测试失败。ServerName={ServerName}, Transport={Transport}, UrlHost={UrlHost}",
                server.Name,
                transport,
                endpoint.Host);
            return BuildFailure("network_error", $"无法连接 MCP 服务：{ex.Message}", transport, stopwatch.ElapsedMilliseconds);
        }
    }

    private static string NormalizeTransport(string? raw)
    {
        var transport = raw?.Trim();
        if (string.IsNullOrWhiteSpace(transport))
        {
            return "streamable-http";
        }

        return string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase)
            ? "streamable-http"
            : transport.ToLowerInvariant();
    }

    private static bool TryCreateEndpoint(string? rawUrl, out Uri endpoint)
    {
        endpoint = null!;
        var trimmed = rawUrl?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }

    private static (string Status, string Message)? ApplyHeaders(
        HiringMcpServerConfigDto server,
        HttpRequestMessage request)
    {
        var bearerEnvName = server.BearerTokenEnv?.Trim();
        if (!string.IsNullOrWhiteSpace(bearerEnvName))
        {
            var token = ResolveSecret(server, bearerEnvName);
            if (string.IsNullOrWhiteSpace(token))
            {
                return ("missing_secret", $"未找到 Bearer 令牌环境变量：{bearerEnvName}");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        foreach (var (headerName, headerValue) in server.Headers)
        {
            if (!TryAddHeader(request, headerName, headerValue))
            {
                return ("invalid_header", $"HTTP Header 名称无效：{headerName}");
            }
        }

        foreach (var (headerName, envName) in server.HeadersFromEnv)
        {
            if (string.IsNullOrWhiteSpace(headerName))
            {
                continue;
            }

            var secret = ResolveSecret(server, envName);
            if (string.IsNullOrWhiteSpace(secret))
            {
                return ("missing_secret", $"未找到 Header {headerName.Trim()} 对应的环境变量：{envName}");
            }

            if (!TryAddHeader(request, headerName, secret))
            {
                return ("invalid_header", $"HTTP Header 名称无效：{headerName}");
            }
        }

        return null;
    }

    private static string? ResolveSecret(HiringMcpServerConfigDto server, string? envName)
    {
        var trimmedEnvName = envName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedEnvName))
        {
            return null;
        }

        if (server.Env.TryGetValue(trimmedEnvName, out var inlineValue))
        {
            return inlineValue;
        }

        return Environment.GetEnvironmentVariable(trimmedEnvName);
    }

    private static bool TryAddHeader(HttpRequestMessage request, string? name, string? value)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return true;
        }

        return request.Headers.TryAddWithoutValidation(trimmedName, value ?? string.Empty);
    }

    private static HiringMcpConnectivityTestResultDto BuildHttpResult(
        HttpStatusCode statusCode,
        long latencyMs,
        string transport)
    {
        var code = (int)statusCode;
        return statusCode switch
        {
            >= HttpStatusCode.OK and < HttpStatusCode.BadRequest => BuildSuccess(
                "connected",
                $"MCP 服务已响应，HTTP {code}。",
                transport,
                code,
                latencyMs),
            HttpStatusCode.MethodNotAllowed => BuildSuccess(
                "endpoint_reachable",
                "MCP 服务可达，但 GET 方法被拒绝；这通常表示端点需要 MCP 协议请求。",
                transport,
                code,
                latencyMs),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => BuildFailure(
                "auth_failed",
                $"MCP 服务可达，但认证未通过，HTTP {code}。",
                transport,
                latencyMs,
                code),
            >= HttpStatusCode.InternalServerError => BuildFailure(
                "server_error",
                $"MCP 服务返回服务端错误，HTTP {code}。",
                transport,
                latencyMs,
                code),
            _ => BuildFailure(
                "http_error",
                $"MCP 服务返回 HTTP {code}，请确认 URL 是否为 MCP 端点。",
                transport,
                latencyMs,
                code)
        };
    }

    private static HiringMcpConnectivityTestResultDto BuildSuccess(
        string status,
        string message,
        string transport,
        int? httpStatusCode,
        long latencyMs)
    {
        return new HiringMcpConnectivityTestResultDto
        {
            Success = true,
            Status = status,
            Message = message,
            HttpStatusCode = httpStatusCode,
            LatencyMs = latencyMs,
            Transport = transport,
            TestedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static HiringMcpConnectivityTestResultDto BuildFailure(
        string status,
        string message,
        string transport = "",
        long? latencyMs = null,
        int? httpStatusCode = null)
    {
        return new HiringMcpConnectivityTestResultDto
        {
            Success = false,
            Status = status,
            Message = message,
            HttpStatusCode = httpStatusCode,
            LatencyMs = latencyMs,
            Transport = transport,
            TestedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
