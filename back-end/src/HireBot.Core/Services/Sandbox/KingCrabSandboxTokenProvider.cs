using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Sandbox;

/// <summary>
/// 沙箱直连（OpenClaw Gateway）所需 access_token 的提供者。
/// 优先使用 OpenSandbox:KingCrab:ClientId / ClientSecret 通过 Keycloak
/// client_credentials 模式获取实时 token，并按 expires_in 缓存；
/// 缺失 client 凭据时回退到静态 OpenSandbox:KingCrab:AuthToken。
/// 单例：跨请求共享 token 缓存，避免每次调用都打 Keycloak。
/// </summary>
internal sealed class KingCrabSandboxTokenProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<KingCrabSandboxTokenProvider> logger)
{
    internal const string TokenHttpClientName = "KingCrabSandboxToken";

    // 提前 30 秒过期，避免临界点上发出请求后被网关判过期
    private static readonly TimeSpan ExpirationSkew = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private CachedToken? cached;

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var snapshot = cached;
        if (snapshot is not null && snapshot.ExpiresAtUtc - ExpirationSkew > DateTimeOffset.UtcNow)
        {
            return snapshot.AccessToken;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            // 双检：可能在排队等待锁期间已被其它请求刷新
            snapshot = cached;
            if (snapshot is not null && snapshot.ExpiresAtUtc - ExpirationSkew > DateTimeOffset.UtcNow)
            {
                return snapshot.AccessToken;
            }

            var fetched = await FetchAsync(cancellationToken);
            if (fetched is not null)
            {
                cached = fetched;
                return fetched.AccessToken;
            }

            // 拉取失败时回退到静态 token（如配置了的话）
            var staticToken = configuration["OpenSandbox:KingCrab:AuthToken"];
            return string.IsNullOrWhiteSpace(staticToken) ? null : staticToken.Trim();
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task<CachedToken?> FetchAsync(CancellationToken cancellationToken)
    {
        var staticToken = GetStaticToken();
        var clientId = configuration["OpenSandbox:KingCrab:ClientId"];
        var clientSecret = configuration["OpenSandbox:KingCrab:ClientSecret"];
        var authority = configuration["OpenSandbox:KingCrab:OidcAuthority"];

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret) ||
            string.IsNullOrWhiteSpace(authority))
        {
            if (staticToken is null)
            {
                logger.LogWarning(
                    "OpenSandbox:KingCrab ClientId/ClientSecret/OidcAuthority 未完整配置，且未配置静态 AuthToken。");
            }

            return null;
        }

        var tokenEndpoint = BuildTokenEndpoint(authority.Trim());
        using var httpClient = httpClientFactory.CreateClient(TokenHttpClientName);

        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId.Trim()),
            new KeyValuePair<string, string>("client_secret", clientSecret.Trim())
        });

        try
        {
            using var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = response.Content is null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Keycloak client_credentials 获取 token 失败. ClientId={ClientId}, Status={Status}, Body={Body}",
                    clientId,
                    (int)response.StatusCode,
                    body);
                return null;
            }

            var payload = await response.Content!.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                logger.LogError("Keycloak 返回了空的 access_token。ClientId={ClientId}", clientId);
                return null;
            }

            // expires_in 为秒；最小给 60s，防御异常返回值
            var lifetime = TimeSpan.FromSeconds(Math.Max(60, payload.ExpiresIn));
            return new CachedToken(payload.AccessToken.Trim(), DateTimeOffset.UtcNow + lifetime);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 Keycloak token 端点异常。Endpoint={Endpoint}", tokenEndpoint);
            return null;
        }
    }

    private static Uri BuildTokenEndpoint(string authority)
    {
        // 兼容用户填写带或不带尾斜杠的 authority
        var trimmed = authority.TrimEnd('/');
        return new Uri($"{trimmed}/protocol/openid-connect/token", UriKind.Absolute);
    }

    private string? GetStaticToken()
    {
        var staticToken = configuration["OpenSandbox:KingCrab:AuthToken"];
        return string.IsNullOrWhiteSpace(staticToken) ? null : staticToken.Trim();
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }
}
