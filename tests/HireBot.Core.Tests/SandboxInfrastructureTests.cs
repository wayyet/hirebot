using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class SandboxInfrastructureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void SandboxProvisioningSettings_FromConfiguration_ShouldThrow_WhenDomainMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120")
            ])
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => SandboxProvisioningSettings.FromConfiguration(configuration));
        Assert.Equal("OpenSandbox:Domain is required.", exception.Message);
    }

    [Fact]
    public void SandboxProvisioningSettings_FromConfiguration_ShouldBuildRuntimeEnvAndNetworkPolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:Domain", "sandbox.example.com"),
                new KeyValuePair<string, string?>("OpenSandbox:Protocol", "Http"),
                new KeyValuePair<string, string?>("OpenSandbox:UseServerProxy", "true"),
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120"),
                new KeyValuePair<string, string?>("OpenSandbox:Resource:cpu", "1500m"),
                new KeyValuePair<string, string?>("OpenSandbox:Resource:memory", "3Gi"),
                new KeyValuePair<string, string?>("OpenSandbox:Entrypoint:0", "/app/OpenClaw.Gateway"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:OidcAuthority", "https://id.example.com"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:OidcAudience", "hirebot"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:ToolTimeoutSeconds", "480"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmModel", "gpt-5.4"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmEndpoint", "https://api.example.com"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmApiKey", "secret"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:NetworkEgressAllowHosts:0", "api.openai.com"),
                new KeyValuePair<string, string?>("AllowedOrigins:Sandbox:0", "https://hirebot.example.com")
            ])
            .Build();

        var settings = SandboxProvisioningSettings.FromConfiguration(configuration);
        var connection = settings.BuildConnection();
        var env = settings.BuildRuntimeEnv();
        var networkPolicy = settings.BuildNetworkPolicy();

        Assert.Equal("sandbox.example.com", settings.Domain);
        Assert.True(settings.UseServerProxy);
        Assert.Equal("registry.local/hirebot-sandbox:latest", settings.Image);
        Assert.Equal(18790, settings.GatewayPort);
        Assert.Equal("1500m", settings.ResourceLimits["cpu"]);
        Assert.Equal("3Gi", settings.ResourceLimits["memory"]);
        Assert.Equal("/app/OpenClaw.Gateway", Assert.Single(settings.Entrypoint));
        Assert.True(connection.UseServerProxy);
        Assert.Equal("sandbox-token", env["OpenClaw__AuthToken"]);
        Assert.Equal("18790", env["OpenClaw__Port"]);
        Assert.Equal("https://hirebot.example.com", env["OpenClaw__Security__AllowedOrigins__0"]);
        Assert.Equal("gpt-5.4", env["MODEL_PROVIDER_MODEL"]);
        Assert.NotNull(networkPolicy);
        Assert.NotNull(networkPolicy!.Egress);
        Assert.Equal("api.openai.com", Assert.Single(networkPolicy.Egress).Target);
    }

    [Fact]
    public void SandboxProvisioningSettings_BuildRuntimeEnv_ShouldThrow_WhenLlmApiKeyMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:Domain", "sandbox.example.com"),
                new KeyValuePair<string, string?>("OpenSandbox:Protocol", "Http"),
                new KeyValuePair<string, string?>("OpenSandbox:UseServerProxy", "true"),
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmModel", "MiniMax-M2.5"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmEndpoint", "https://api.minimaxi.com/v1")
            ])
            .Build();

        var settings = SandboxProvisioningSettings.FromConfiguration(configuration);

        var exception = Assert.Throws<InvalidOperationException>(() => settings.BuildRuntimeEnv());
        Assert.Equal(
            "OpenSandbox:KingCrab:LlmModel, OpenSandbox:KingCrab:LlmEndpoint, and OpenSandbox:KingCrab:LlmApiKey must be configured together.",
            exception.Message);
    }

    [Theory]
    [InlineData(false, "http://sandbox.example.com/sandboxes/sandbox-001/endpoints/18790")]
    [InlineData(true, "http://sandbox.example.com/sandboxes/sandbox-001/endpoints/18790?use_server_proxy=true")]
    public void OpenSandboxProvisioner_BuildEndpointLookupUrl_ShouldHonorServerProxySetting(bool useServerProxy, string expected)
    {
        var actual = OpenSandboxProvisioner.BuildEndpointLookupUrl(
            "http://sandbox.example.com",
            "sandbox-001",
            18790,
            useServerProxy);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task OpenSandboxProvisioner_RefreshAsync_ShouldResolveDirectGatewayEndpoint_WhenServerProxyEnabled()
    {
        const string directGatewayEndpoint = "127.0.0.1:45818/proxy/18789";
        await using var endpointServer = await OpenSandboxEndpointServer.StartAsync(directGatewayEndpoint);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:Domain", $"127.0.0.1:{endpointServer.Port}"),
                new KeyValuePair<string, string?>("OpenSandbox:Protocol", "Http"),
                new KeyValuePair<string, string?>("OpenSandbox:UseServerProxy", "true"),
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        var scopeFactory = new ServiceCollection()
            .AddDbContext<HireBotDbContext>(options => options.UseInMemoryDatabase("provisioner-refresh-" + Guid.NewGuid()))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var provisioner = new OpenSandboxProvisioner(
            configuration,
            scopeFactory,
            new SandboxPvcService(configuration, NullLogger<SandboxPvcService>.Instance),
            NullLogger<OpenSandboxProvisioner>.Instance);

        var result = await provisioner.RefreshAsync("sandbox-001");

        Assert.Equal("Running", result.State);
        Assert.Equal(directGatewayEndpoint, result.GatewayEndpoint);
        Assert.Contains(
            endpointServer.Requests,
            request => request.EndsWith("/sandboxes/sandbox-001", StringComparison.OrdinalIgnoreCase));

        var endpointLookupRequests = endpointServer.Requests
            .Where(request => request.Contains("/sandboxes/sandbox-001/endpoints/18790", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(endpointLookupRequests);
        Assert.All(
            endpointLookupRequests,
            request => Assert.DoesNotContain("use_server_proxy=true", request, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenSandboxProvisioner_BuildCreateOptions_ShouldSkipSdkHealthCheck_AndPreserveOwnerMetadata()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:Domain", "sandbox.example.com"),
                new KeyValuePair<string, string?>("OpenSandbox:Protocol", "Http"),
                new KeyValuePair<string, string?>("OpenSandbox:UseServerProxy", "true"),
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmModel", "MiniMax-M2.5"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmEndpoint", "https://api.minimaxi.com/v1"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:LlmApiKey", "secret")
            ])
            .Build();

        var settings = SandboxProvisioningSettings.FromConfiguration(configuration);
        OpenSandbox.Models.Volume[] volumes =
        [
            new OpenSandbox.Models.Volume
            {
                Name = "kc-workspace",
                Pvc = new OpenSandbox.Models.PVC { ClaimName = "kc-ws-tenant-1-operator-1" },
                MountPath = "/workspace"
            }
        ];

        var options = OpenSandboxProvisioner.BuildCreateOptions(settings, "tenant-1:operator-1", volumes);

        Assert.True(options.SkipHealthCheck);
        Assert.True(options.ManualCleanup);
        Assert.Equal("tenant-1-operator-1", options.Metadata["owner"]);
        Assert.Equal("/workspace", Assert.Single(options.Volumes!).MountPath);
    }

    [Fact]
    public async Task KingCrabHttpClient_SendMultipartForJsonAsync_ShouldAppendUploadPathFromGatewayEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(new EchoResult("ok")));
        var httpClient = new HttpClient(handler);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        var client = new KingCrabHttpClient(
            new StubHttpClientFactory(httpClient),
            configuration,
            CreateSandboxTokenProvider(new StubHttpClientFactory(httpClient), configuration),
            NullLogger<KingCrabHttpClient>.Instance);

        var result = await client.SendMultipartForJsonAsync<EchoResult>(
            "/admin/digital-employee/upload",
            "file",
            "demo.zip",
            Encoding.UTF8.GetBytes("zip"),
            "application/zip",
            "tenant-a:operator-b",
            CancellationToken.None,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: "183.6.65.92:90/d9424116-2b57-496f-9cb8-5a972c557de0/18789");

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/d9424116-2b57-496f-9cb8-5a972c557de0/18789/admin/digital-employee/upload", request.Path);
        Assert.Equal("183.6.65.92", request.Host);
        Assert.Equal("sandbox-token", request.AuthorizationBearerToken);
    }

    [Fact]
    public void SandboxPvcService_BuildVolumes_ShouldUseOwnerScopedPvcNamesAndMountPaths()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("SandboxPvc:Enabled", "false")
            ])
            .Build();

        var service = new SandboxPvcService(configuration, NullLogger<SandboxPvcService>.Instance);
        var volumes = service.BuildVolumes("Tenant_A:Operator_B");

        Assert.Equal("kc-ws-tenant-a-operator-b", service.WorkspacePvcName("Tenant_A:Operator_B"));
        Assert.Equal("kc-mem-tenant-a-operator-b", service.MemoryPvcName("Tenant_A:Operator_B"));
        Assert.Collection(
            volumes,
            workspace =>
            {
                Assert.Equal("kc-workspace", workspace.Name);
                Assert.Equal("/workspace", workspace.MountPath);
                Assert.Equal("kc-ws-tenant-a-operator-b", workspace.Pvc!.ClaimName);
            },
            memory =>
            {
                Assert.Equal("kc-memory", memory.Name);
                Assert.Equal("/app/memory", memory.MountPath);
                Assert.Equal("kc-mem-tenant-a-operator-b", memory.Pvc!.ClaimName);
            });
    }

    [Fact]
    public async Task KingCrabHttpClient_SendForJsonAsync_ShouldIgnoreIncomingAuthorizationHeaderWhenNoServiceTokenConfigured()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(new EchoResult("ok")));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://kingcrab.local/")
        };

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpContextAccessor.HttpContext.Request.Headers.Authorization = "Bearer inbound-token";

        var httpClientFactory = new StubHttpClientFactory(httpClient);
        var client = new KingCrabHttpClient(
            httpClientFactory,
            new ConfigurationBuilder().Build(),
            CreateSandboxTokenProvider(httpClientFactory, new ConfigurationBuilder().Build()),
            NullLogger<KingCrabHttpClient>.Instance);

        var result = await client.SendForJsonAsync<EchoResult>(
            HttpMethod.Get,
            "/ping",
            body: null,
            "tenant-a:operator-b",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("ok", result.Data!.Value);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/integration/hirebot/ping", request.Path);
        Assert.Null(request.Authorization);
        Assert.Equal("tenant-a:operator-b", request.OwnerHeader);
    }

    [Fact]
    public async Task KingCrabHttpClient_SendForJsonAsync_ShouldUseStaticBearerTokenWhenNoIncomingAuthorization()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(new EchoResult("ok")));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://kingcrab.local/")
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BearerToken", "static-token")
            ])
            .Build();

        var httpClientFactory = new StubHttpClientFactory(httpClient);
        var client = new KingCrabHttpClient(
            httpClientFactory,
            configuration,
            CreateSandboxTokenProvider(httpClientFactory, configuration),
            NullLogger<KingCrabHttpClient>.Instance);

        var result = await client.SendForJsonAsync<EchoResult>(
            HttpMethod.Get,
            "/ping",
            body: null,
            "tenant-a:operator-b",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("static-token", Assert.Single(handler.Requests).AuthorizationBearerToken);
    }

    [Fact]
    public async Task KingCrabHttpClient_SendMultipartForJsonAsync_ShouldHonorAbsoluteUploadUrl()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(new EchoResult("ok")));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://kingcrab.local/")
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        var httpClientFactory = new StubHttpClientFactory(httpClient);
        var client = new KingCrabHttpClient(
            httpClientFactory,
            configuration,
            CreateSandboxTokenProvider(httpClientFactory, configuration),
            NullLogger<KingCrabHttpClient>.Instance);

        var result = await client.SendMultipartForJsonAsync<EchoResult>(
            "http://sandbox-gateway.local/0392ec86-a659-4816-a7af-c6288285df1b/18789/admin/digital-employee/upload",
            "file",
            "skill.zip",
            [0x01, 0x02, 0x03],
            "application/zip",
            "tenant-a:operator-b",
            CancellationToken.None,
            useHireBotApiPrefix: false);

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("sandbox-gateway.local", request.Host);
        Assert.Equal("/0392ec86-a659-4816-a7af-c6288285df1b/18789/admin/digital-employee/upload", request.Path);
        Assert.Equal("sandbox-token", request.AuthorizationBearerToken);
    }

    [Fact]
    public async Task KingCrabHttpClient_SendMultipartForJsonAsync_ShouldUseSandboxTokenProviderForOidcSandboxGateway()
    {
        var uploadHandler = new RecordingHttpMessageHandler(_ => JsonResponse(new EchoResult("ok")));
        var httpClient = new HttpClient(uploadHandler)
        {
            BaseAddress = new Uri("http://kingcrab.local/")
        };
        var tokenHandler = new RecordingHttpMessageHandler(_ => JsonResponse(new
        {
            access_token = "sandbox-access-token",
            expires_in = 600,
            token_type = "Bearer"
        }));
        var tokenHttpClient = new HttpClient(tokenHandler);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:OidcAuthority", "http://id.example.com/realms/test"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:ClientId", "sandbox-client"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:ClientSecret", "sandbox-secret")
            ])
            .Build();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpContextAccessor.HttpContext.Request.Headers.Authorization = "Bearer inbound-token";

        var httpClientFactory = new StubHttpClientFactory(
            httpClient,
            new Dictionary<string, HttpClient>(StringComparer.Ordinal)
            {
                [KingCrabSandboxTokenProvider.TokenHttpClientName] = tokenHttpClient
            });
        var client = new KingCrabHttpClient(
            httpClientFactory,
            configuration,
            CreateSandboxTokenProvider(httpClientFactory, configuration),
            NullLogger<KingCrabHttpClient>.Instance);

        var result = await client.SendMultipartForJsonAsync<EchoResult>(
            "http://sandbox-gateway.local/0392ec86-a659-4816-a7af-c6288285df1b/18789/admin/digital-employee/upload",
            "file",
            "skill.zip",
            [0x01, 0x02, 0x03],
            "application/zip",
            "tenant-a:operator-b",
            CancellationToken.None,
            useHireBotApiPrefix: false);

        Assert.True(result.Success);
        var request = Assert.Single(uploadHandler.Requests);
        Assert.Equal("Bearer sandbox-access-token", request.Authorization);
        Assert.Equal("sandbox-access-token", request.AuthorizationBearerToken);

        var tokenRequest = Assert.Single(tokenHandler.Requests);
        Assert.Equal("id.example.com", tokenRequest.Host);
        Assert.Equal("/realms/test/protocol/openid-connect/token", tokenRequest.Path);
    }

    [Fact]
    public async Task KingCrabSandboxTokenProvider_GetAccessTokenAsync_ShouldFallbackToStaticToken_WhenOidcClientCredentialsRejected()
    {
        var tokenHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"unauthorized_client\"}", Encoding.UTF8, "application/json")
        });
        var tokenHttpClient = new HttpClient(tokenHandler);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-static-token"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:OidcAuthority", "http://id.example.com/realms/test"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:ClientId", "sandbox-client"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:ClientSecret", "sandbox-secret")
            ])
            .Build();

        var httpClientFactory = new StubHttpClientFactory(
            new HttpClient(new RecordingHttpMessageHandler(_ => JsonResponse(new EchoResult("unused")))),
            new Dictionary<string, HttpClient>(StringComparer.Ordinal)
            {
                [KingCrabSandboxTokenProvider.TokenHttpClientName] = tokenHttpClient
            });
        var provider = CreateSandboxTokenProvider(httpClientFactory, configuration);

        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("sandbox-static-token", token);
        var tokenRequest = Assert.Single(tokenHandler.Requests);
        Assert.Equal("id.example.com", tokenRequest.Host);
        Assert.Equal("/realms/test/protocol/openid-connect/token", tokenRequest.Path);
    }

    [Fact]
    public async Task SandboxService_SendMessageAsync_ShouldRecoverPersistedBindingsAndInjectFileMarker()
    {
        const string databaseName = "sandbox-service-restart-recovery";
        var databaseRoot = new InMemoryDatabaseRoot();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://kingcrab.local/"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        using (var firstContext = CreateDbContext(databaseName, databaseRoot))
        {
            var firstHandler = new RecordingHttpMessageHandler(BuildSandboxApiResponse);
            var firstService = CreateSandboxService(firstContext, configuration, firstHandler);

            var registerResult = await firstService.RegisterAsync(
                new SandboxRegisterRequestDto
                {
                    SandboxId = "sandbox-001",
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = "hire-001",
                    SandboxRole = "hiring",
                    OwnerSubject = "tenant-1:operator-1",
                    TenantId = "tenant-1",
                    OperatorId = "operator-1",
                    ProvisioningMode = "external",
                    State = "Running",
                    GatewayEndpoint = "http://sandbox-gateway.local/"
                });

            Assert.True(registerResult.Success);

            var ensureSessionResult = await firstService.EnsureSessionAsync(
                new SandboxEnsureSessionRequestDto
                {
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = "hire-001",
                    SandboxRole = "hiring",
                    OwnerSubject = "tenant-1:operator-1",
                    TenantId = "tenant-1",
                    OperatorId = "operator-1",
                    SessionKey = "default"
                });

            Assert.True(ensureSessionResult.Success);
            Assert.StartsWith("session-", ensureSessionResult.Data!.SessionId);
        }

        using (var secondContext = CreateDbContext(databaseName, databaseRoot))
        {
            var secondHandler = new RecordingHttpMessageHandler(BuildSandboxApiResponse);
            var secondService = CreateSandboxService(secondContext, configuration, secondHandler);

            var sendResult = await secondService.SendMessageAsync(
                new SandboxSendMessageRequestDto
                {
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = "hire-001",
                    SandboxRole = "hiring",
                    OwnerSubject = "tenant-1:operator-1",
                    TenantId = "tenant-1",
                    OperatorId = "operator-1",
                    SessionKey = "default",
                    Content = "鐠囬婀呴梽鍕",
                    Materials =
                    [
                        new HiringConversationMaterialDto
                        {
                            Type = "file",
                            Name = "brief.txt",
                            MimeType = "text/plain",
                            Content = Convert.ToBase64String(Encoding.UTF8.GetBytes("brief-content")),
                            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["contentEncoding"] = "base64"
                            }
                        }
                    ]
                });

            Assert.True(sendResult.Success);
            Assert.False(string.IsNullOrWhiteSpace(secondHandler.LastChatCompletionContent));
            Assert.Contains("[FILE_URL:/app/memory/media-cache/media-001]", secondHandler.LastChatCompletionContent!);

            var uploadRequest = Assert.Single(secondHandler.Requests, item => item.Path == "/media/upload");
            Assert.Equal("sandbox-gateway.local", uploadRequest.Host);
            Assert.Equal("sandbox-token", uploadRequest.AuthorizationBearerToken);

            var chatRequest = Assert.Single(secondHandler.Requests, item => item.Path == "/v1/chat/completions");
            Assert.Equal("sandbox-gateway.local", chatRequest.Host);
            Assert.Equal("sandbox-token", chatRequest.AuthorizationBearerToken);
            Assert.StartsWith("session-", chatRequest.SessionHeader);

            var persistedAsset = await secondContext.SandboxAssets.SingleAsync();
            Assert.Equal("media-001", persistedAsset.MediaId);
            Assert.Equal("http://sandbox-gateway.local/media/media-001", persistedAsset.Url);
            Assert.NotNull(persistedAsset.SandboxInstanceEntityId);
            Assert.NotNull(persistedAsset.SandboxSessionEntityId);
        }
    }

    [Fact]
    public async Task SandboxService_SendMessageAsync_WithTwoAttachments_ShouldInjectTwoFileMarkers()
    {
        const string databaseName = "sandbox-service-two-attachments";
        var databaseRoot = new InMemoryDatabaseRoot();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://kingcrab.local/"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        var uploadIndex = 0;
        HttpResponseMessage Responder(CapturedRequest request)
        {
            if (request.Path.EndsWith("/media/upload", StringComparison.OrdinalIgnoreCase))
            {
                uploadIndex++;
                return JsonResponse(new
                {
                    id = $"media-00{uploadIndex}",
                    url = $"/media/media-00{uploadIndex}",
                    fileName = uploadIndex == 1 ? "reference.zip" : "reference-template-summary.md",
                    mimeType = uploadIndex == 1 ? "application/zip" : "text/markdown",
                    sizeBytes = uploadIndex == 1 ? 128L : 64L
                });
            }

            return BuildSandboxApiResponse(request);
        }

        using var dbContext = CreateDbContext(databaseName, databaseRoot);
        var handler = new RecordingHttpMessageHandler(Responder);
        var service = CreateSandboxService(dbContext, configuration, handler);

        var registerResult = await service.RegisterAsync(
            new SandboxRegisterRequestDto
            {
                SandboxId = "sandbox-001",
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                ProvisioningMode = "external",
                State = "Running",
                GatewayEndpoint = "http://sandbox-gateway.local/"
            });

        Assert.True(registerResult.Success);

        var sendResult = await service.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                Content = "请先阅读参考模板附件。",
                Materials =
                [
                    new HiringConversationMaterialDto
                    {
                        Type = "file",
                        Name = "reference.zip",
                        MimeType = "application/zip",
                        Content = Convert.ToBase64String(Encoding.UTF8.GetBytes("zip-bytes")),
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["contentEncoding"] = "base64"
                        }
                    },
                    new HiringConversationMaterialDto
                    {
                        Type = "document",
                        Name = "reference-template-summary.md",
                        MimeType = "text/markdown",
                        Content = "# summary"
                    }
                ]
            });

        Assert.True(sendResult.Success);
        Assert.False(string.IsNullOrWhiteSpace(handler.LastChatCompletionContent));
        Assert.Contains("请先阅读参考模板附件。", handler.LastChatCompletionContent!, StringComparison.Ordinal);
        Assert.Contains("[FILE_URL:/app/memory/media-cache/media-001]", handler.LastChatCompletionContent!, StringComparison.Ordinal);
        Assert.Contains("[FILE_URL:/app/memory/media-cache/media-002]", handler.LastChatCompletionContent!, StringComparison.Ordinal);

        var uploadRequests = handler.Requests
            .Where(item => item.Path == "/media/upload")
            .ToArray();
        Assert.Equal(2, uploadRequests.Length);
    }

    [Fact]
    public async Task SandboxService_ConversationLifecycle_ShouldStartSendAndReadTimeline()
    {
        const string databaseName = "sandbox-service-conversation-lifecycle";
        const string directGatewayEndpoint = "sandbox-direct.local/runtime/sandbox-001/18790";
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var endpointServer = await OpenSandboxEndpointServer.StartAsync(directGatewayEndpoint);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://kingcrab.local/"),
                new KeyValuePair<string, string?>("OpenSandbox:Domain", $"127.0.0.1:{endpointServer.Port}"),
                new KeyValuePair<string, string?>("OpenSandbox:Protocol", "Http"),
                new KeyValuePair<string, string?>("OpenSandbox:UseServerProxy", "true"),
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        using var dbContext = CreateDbContext(databaseName, databaseRoot);
        var handler = new RecordingHttpMessageHandler(BuildSandboxApiResponse);
        var service = CreateSandboxService(dbContext, configuration, handler);

        var registerResult = await service.RegisterAsync(
            new SandboxRegisterRequestDto
            {
                SandboxId = "sandbox-001",
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                ProvisioningMode = "managed",
                State = "Running",
                GatewayEndpoint = "http://127.0.0.1:8080/sandboxes/sandbox-001/proxy/18790"
            });

        Assert.True(registerResult.Success);

        var ensureSessionResult = await service.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001"
            });

        Assert.True(ensureSessionResult.Success);
        var sessionId = ensureSessionResult.Data!.SessionId;
        Assert.StartsWith("session-", sessionId);

        var sendResult = await service.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001",
                Content = "Please continue."
            });

        Assert.True(sendResult.Success);
        Assert.Equal(sessionId, sendResult.Data!.SessionId);
        Assert.Equal("assistant", sendResult.Data.AssistantMessage.Role);
        Assert.False(string.IsNullOrWhiteSpace(sendResult.Data.AssistantMessage.Content));

        var timelineResult = await service.GetTimelineAsync(
            new SandboxTimelineRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001"
            });

        Assert.True(
            timelineResult.Success,
            $"{timelineResult.Code}:{timelineResult.Message}; gateway={string.Join("|", handler.Requests.Select(static request => request.Path))}; endpoint={string.Join("|", endpointServer.Requests)}");
        Assert.Equal(sessionId, timelineResult.Data!.SessionId);
        Assert.Equal(2, timelineResult.Data.Messages.Count);
        Assert.Equal("user", timelineResult.Data.Messages[0].Role);
        Assert.Equal("assistant", timelineResult.Data.Messages[1].Role);

        var chatRequest = Assert.Single(
            handler.Requests,
            request => request.Method == HttpMethod.Post &&
                       request.Path.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sandbox-direct.local", chatRequest.Host);
        Assert.Equal("/runtime/sandbox-001/18790/v1/chat/completions", chatRequest.Path);
        Assert.Equal("sandbox-token", chatRequest.AuthorizationBearerToken);
        Assert.Equal(sessionId, chatRequest.SessionHeader);

        var historyRequest = Assert.Single(
            handler.Requests,
            request => request.Method == HttpMethod.Get &&
                       request.Path.EndsWith($"/api/integration/sessions/{sessionId}", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sandbox-direct.local", historyRequest.Host);
        Assert.Equal($"/runtime/sandbox-001/18790/api/integration/sessions/{sessionId}", historyRequest.Path);
        Assert.Equal("sandbox-token", historyRequest.AuthorizationBearerToken);

        var endpointLookupRequests = endpointServer.Requests
            .Where(request => request.Contains("/sandboxes/sandbox-001/endpoints/18790", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(endpointLookupRequests);
        Assert.All(
            endpointLookupRequests,
            request => Assert.DoesNotContain("use_server_proxy=true", request, StringComparison.OrdinalIgnoreCase));

        var persistedInstance = await dbContext.SandboxInstances.SingleAsync();
        Assert.Equal(directGatewayEndpoint, persistedInstance.GatewayEndpoint);

        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains("/conversation/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SandboxService_GetSessionDetailAsync_ShouldReturnTodoMetadata()
    {
        const string databaseName = "sandbox-service-session-detail-metadata";
        const string directGatewayEndpoint = "sandbox-direct.local/runtime/sandbox-001/18790";
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var endpointServer = await OpenSandboxEndpointServer.StartAsync(directGatewayEndpoint);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://kingcrab.local/"),
                new KeyValuePair<string, string?>("OpenSandbox:Domain", $"127.0.0.1:{endpointServer.Port}"),
                new KeyValuePair<string, string?>("OpenSandbox:Protocol", "Http"),
                new KeyValuePair<string, string?>("OpenSandbox:UseServerProxy", "true"),
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        using var dbContext = CreateDbContext(databaseName, databaseRoot);
        var handler = new RecordingHttpMessageHandler(BuildSandboxApiResponse);
        var service = CreateSandboxService(dbContext, configuration, handler);

        var registerResult = await service.RegisterAsync(
            new SandboxRegisterRequestDto
            {
                SandboxId = "sandbox-001",
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                ProvisioningMode = "managed",
                State = "Running",
                GatewayEndpoint = "http://127.0.0.1:8080/sandboxes/sandbox-001/proxy/18790"
            });

        Assert.True(registerResult.Success);

        var detailResult = await service.GetSessionDetailAsync(
            new SandboxSessionDetailRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001"
            });

        Assert.True(detailResult.Success);
        var todo = Assert.Single(detailResult.Data!.TodoItems);
        Assert.Equal("todo_material_001", todo.Id);
        Assert.True(todo.Completed);
        Assert.Contains("ontology_extraction", todo.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SandboxService_GetTimelineAsync_ShouldSurfaceOpenSandboxEndpointLookupFailure()
    {
        const string databaseName = "sandbox-service-timeline-endpoint-lookup-failure";
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var endpointServer = await OpenSandboxEndpointServer.StartUnavailableAsync(HttpStatusCode.ServiceUnavailable);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://kingcrab.local/"),
                new KeyValuePair<string, string?>("OpenSandbox:Domain", $"127.0.0.1:{endpointServer.Port}"),
                new KeyValuePair<string, string?>("OpenSandbox:Protocol", "Http"),
                new KeyValuePair<string, string?>("OpenSandbox:UseServerProxy", "true"),
                new KeyValuePair<string, string?>("OpenSandbox:Image", "registry.local/hirebot-sandbox:latest"),
                new KeyValuePair<string, string?>("OpenSandbox:GatewayPort", "18790"),
                new KeyValuePair<string, string?>("OpenSandbox:TimeoutSeconds", "3600"),
                new KeyValuePair<string, string?>("OpenSandbox:ReadyTimeoutSeconds", "120"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        using var dbContext = CreateDbContext(databaseName, databaseRoot);
        var handler = new RecordingHttpMessageHandler(BuildSandboxApiResponse);
        var service = CreateSandboxService(dbContext, configuration, handler);

        var registerResult = await service.RegisterAsync(
            new SandboxRegisterRequestDto
            {
                SandboxId = "sandbox-001",
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                ProvisioningMode = "external",
                State = "Running",
                GatewayEndpoint = "http://sandbox-gateway.local/"
            });

        Assert.True(registerResult.Success);

        var timelineResult = await service.GetTimelineAsync(
            new SandboxTimelineRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001"
            });

        Assert.False(timelineResult.Success);
        Assert.Equal(503, timelineResult.Code);
        Assert.Equal("OpenSandbox endpoint lookup failed (HTTP 503)", timelineResult.Message);

        var endpointLookupRequest = Assert.Single(endpointServer.Requests);
        Assert.EndsWith("/sandboxes/sandbox-001/endpoints/18790", endpointLookupRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("use_server_proxy=true", endpointLookupRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SandboxService_ConversationOperations_ShouldFailFast_WhenGatewayEndpointMissing()
    {
        const string databaseName = "sandbox-service-missing-gateway-endpoint";
        var databaseRoot = new InMemoryDatabaseRoot();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://kingcrab.local/"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token")
            ])
            .Build();

        using var dbContext = CreateDbContext(databaseName, databaseRoot);
        var handler = new RecordingHttpMessageHandler(BuildSandboxApiResponse);
        var service = CreateSandboxService(dbContext, configuration, handler);

        var registerResult = await service.RegisterAsync(
            new SandboxRegisterRequestDto
            {
                SandboxId = "sandbox-001",
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                ProvisioningMode = "external",
                State = "Running"
            });

        Assert.True(registerResult.Success);

        var ensureSessionResult = await service.EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001"
            });
        Assert.False(ensureSessionResult.Success);
        Assert.Equal(409, ensureSessionResult.Code);

        var sendResult = await service.SendMessageAsync(
            new SandboxSendMessageRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001",
                Content = "hello"
            });
        Assert.False(sendResult.Success);
        Assert.Equal(409, sendResult.Code);

        var timelineResult = await service.GetTimelineAsync(
            new SandboxTimelineRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = "hire-001",
                SandboxRole = "hiring",
                OwnerSubject = "tenant-1:operator-1",
                TenantId = "tenant-1",
                OperatorId = "operator-1",
                SessionKey = "default",
                SandboxId = "sandbox-001"
            });
        Assert.False(timelineResult.Success);
        Assert.Equal(409, timelineResult.Code);

        Assert.Empty(handler.Requests);
    }

    private static HireBotDbContext CreateDbContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new HireBotDbContext(options);
    }

    private static SandboxService CreateSandboxService(
        HireBotDbContext dbContext,
        IConfiguration configuration,
        RecordingHttpMessageHandler handler)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = CreateHttpContext()
        };

        var httpClientFactory = new StubHttpClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://kingcrab.local/")
        });
        var kingCrabHttpClient = new KingCrabHttpClient(
            httpClientFactory,
            configuration,
            CreateSandboxTokenProvider(httpClientFactory, configuration),
            NullLogger<KingCrabHttpClient>.Instance);

        var scopeFactory = new ServiceCollection()
            .AddDbContext<HireBotDbContext>(options => options.UseInMemoryDatabase("provisioner-" + Guid.NewGuid()))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var provisioner = new OpenSandboxProvisioner(
            configuration,
            scopeFactory,
            new SandboxPvcService(configuration, NullLogger<SandboxPvcService>.Instance),
            NullLogger<OpenSandboxProvisioner>.Instance);

        return new SandboxService(
            dbContext,
            provisioner,
            kingCrabHttpClient,
            new KingCrabGatewayClient(kingCrabHttpClient),
            NullLogger<SandboxService>.Instance);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "tenant-1:operator-1"),
            new Claim("tenant_id", "tenant-1"),
            new Claim("preferred_username", "operator-1")
        ], "test"));
        return context;
    }

    private static HttpResponseMessage BuildSandboxApiResponse(CapturedRequest request)
    {
        if (request.Path.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "閺€璺哄煂"
                        }
                    }
                }
            });
        }

        if (request.Path.Contains("/api/integration/sessions/", StringComparison.OrdinalIgnoreCase) &&
            request.Method == HttpMethod.Get)
        {
            return JsonResponse(new
            {
                session = new
                {
                    history = new object[]
                    {
                        new
                        {
                            role = "user",
                            content = "Please continue.",
                            timestamp = DateTimeOffset.UtcNow
                        },
                        new
                        {
                            role = "assistant",
                            content = "Received.",
                            timestamp = DateTimeOffset.UtcNow
                        }
                    }
                },
                metadata = new
                {
                    todoItems = new object[]
                    {
                        new
                        {
                            id = "todo_material_001",
                            text = "资料归类",
                            notes = BuildTodoNotesJson(
                                stage: HiringCollectionStage.Material,
                                targetSkill: "ontology_extraction",
                                intent: "整理客服退货流程资料",
                                category: "流程 SOP",
                                status: HiringTodoStatus.Done,
                                source: "用户上传的客服退货流程资料",
                                acceptance: "能够抽出退货流程节点",
                                payloadJson: "{\"objective\":\"抽出退货流程节点\"}"),
                            completed = true,
                            createdAtUtc = DateTimeOffset.Parse("2026-05-06T10:00:00Z"),
                            updatedAtUtc = DateTimeOffset.Parse("2026-05-06T10:05:00Z")
                        }
                    }
                }
            });
        }

        if (request.Path.Contains("/conversation/messages", StringComparison.OrdinalIgnoreCase) &&
            request.Method == HttpMethod.Post)
        {
            return JsonResponse(new HiringConversationResultDto(
                "hire-001",
                "session-001",
                "goal",
                false,
                new HiringConversationMessageDto("msg-001", "assistant", "閺€璺哄煂", DateTimeOffset.UtcNow),
                new HiringStagePreviewDto(
                    "hire-001",
                    "goal",
                    "discovery",
                    "summary",
                    new Dictionary<string, string?>(),
                    [],
                    [],
                    false,
                    DateTimeOffset.UtcNow)));
        }

        if (request.Path.EndsWith("/media/upload", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse(new
            {
                id = "media-001",
                url = "/media/media-001",
                fileName = "brief.txt",
                mimeType = "text/plain",
                sizeBytes = 13L
            });
        }

        throw new InvalidOperationException($"Unexpected request path: {request.Path}");
    }

    private static HttpResponseMessage JsonResponse<T>(T payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private static string BuildTodoNotesJson(
        string stage,
        string kind = HiringTodoKind.Gap,
        string status = HiringTodoStatus.Open,
        string source = "test-source",
        string? targetSkill = null,
        string? intent = null,
        string? acceptance = null,
        string? category = null,
        string? gapType = null,
        string? priority = null,
        string? currentState = null,
        string? expectedState = null,
        string? acceptanceCriteria = null,
        string? acceptanceEvidence = null,
        string? fingerprint = null,
        string? level = null,
        string? question = null,
        string? evidence = null,
        string? suggestedAction = null,
        string? payloadJson = null,
        string[]? relatedTodoIds = null,
        string[]? relatedFiles = null,
        string createdAtUtc = "2026-05-06T10:00:00Z",
        string updatedAtUtc = "2026-05-06T10:05:00Z")
    {
        gapType ??= ResolveGapType(targetSkill);
        priority ??= HiringTodoPriority.Required;
        currentState ??= source;
        expectedState ??= intent ?? source;
        acceptanceCriteria ??= acceptance ?? "存在对应产物即可";
        fingerprint ??= BuildTestTodoFingerprint(stage, targetSkill, kind);
        var payload = payloadJson is null ? "null" : payloadJson;
        var relatedTodos = JsonSerializer.Serialize(relatedTodoIds ?? []);
        var relatedFilesJson = JsonSerializer.Serialize(relatedFiles ?? []);
        return $$"""
        {
          "stage": "{{stage}}",
          "kind": "{{kind}}",
          "gap_type": {{JsonSerializer.Serialize(gapType)}},
          "priority": {{JsonSerializer.Serialize(priority)}},
          "current_state": {{JsonSerializer.Serialize(currentState)}},
          "expected_state": {{JsonSerializer.Serialize(expectedState)}},
          "acceptance_criteria": {{JsonSerializer.Serialize(acceptanceCriteria)}},
          "acceptance_evidence": {{JsonSerializer.Serialize(acceptanceEvidence)}},
          "category": {{JsonSerializer.Serialize(category)}},
          "status": "{{status}}",
          "source": "{{source}}",
          "fingerprint": {{JsonSerializer.Serialize(fingerprint)}},
          "payload": {{payload}},
          "level": {{JsonSerializer.Serialize(level)}},
          "question": {{JsonSerializer.Serialize(question)}},
          "evidence": {{JsonSerializer.Serialize(evidence)}},
          "suggested_action": {{JsonSerializer.Serialize(suggestedAction)}},
          "related_todos": {{relatedTodos}},
          "related_files": {{relatedFilesJson}},
          "created_at": "{{createdAtUtc}}",
          "updated_at": "{{updatedAtUtc}}"
        }
        """;
    }

    private static string ResolveGapType(string? targetSkill)
    {
        return targetSkill?.Trim() switch
        {
            "skill_generation" or "skill-generation" => "missing_skill_definition",
            "external_config" or "external-config" => "missing_external_config",
            _ => "ontology_slice"
        };
    }

    private static string BuildTestTodoFingerprint(string stage, string? targetSkill, string kind)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(targetSkill)
            ? "workflow"
            : targetSkill.Trim().Replace('-', '_').ToLowerInvariant();
        return $"{stage}:{normalizedTarget}:{kind}-001";
    }

    private static KingCrabSandboxTokenProvider CreateSandboxTokenProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        return new KingCrabSandboxTokenProvider(
            httpClientFactory,
            configuration,
            NullLogger<KingCrabSandboxTokenProvider>.Instance);
    }

    private sealed record EchoResult(string Value);

    private sealed class StubHttpClientFactory(
        HttpClient defaultClient,
        IReadOnlyDictionary<string, HttpClient>? namedClients = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            if (namedClients is not null && namedClients.TryGetValue(name, out var namedClient))
            {
                return namedClient;
            }

            return defaultClient;
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string Host,
        string? Authorization,
        string? AuthorizationBearerToken,
        string? OwnerHeader,
        string? SessionHeader,
        string? Content,
        string? ContentType);

    private sealed class RecordingHttpMessageHandler(Func<CapturedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public string? LastChatCompletionContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Host,
                request.Headers.TryGetValues("Authorization", out var authorizationValues) ? authorizationValues.SingleOrDefault() : null,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("X-HireBot-Owner", out var ownerValues) ? ownerValues.SingleOrDefault() : null,
                request.Headers.TryGetValues("X-OpenClaw-Session-Id", out var sessionValues) ? sessionValues.SingleOrDefault() : null,
                content,
                request.Content?.Headers.ContentType?.MediaType);

            Requests.Add(captured);

            if (request.Method == HttpMethod.Post &&
                request.RequestUri.AbsolutePath.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(content))
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("messages", out var messagesElement) &&
                    messagesElement.ValueKind == JsonValueKind.Array &&
                    messagesElement.GetArrayLength() > 0 &&
                    messagesElement[0].TryGetProperty("content", out var contentElement))
                {
                    LastChatCompletionContent = contentElement.GetString();
                }
            }

            return responder(captured);
        }
    }

    private sealed class OpenSandboxEndpointServer : IAsyncDisposable
    {
        private readonly HttpListener listener;
        private readonly Task processingTask;
        private readonly string? endpoint;
        private readonly HttpStatusCode responseStatusCode;

        private OpenSandboxEndpointServer(HttpListener listener, string? endpoint, HttpStatusCode responseStatusCode, int port)
        {
            this.listener = listener;
            this.endpoint = endpoint;
            this.responseStatusCode = responseStatusCode;
            Port = port;
            processingTask = Task.Run(ProcessAsync);
        }

        public int Port { get; }

        public List<string> Requests { get; } = [];

        public static Task<OpenSandboxEndpointServer> StartAsync(string endpoint)
        {
            var tcpListener = new TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            return Task.FromResult(new OpenSandboxEndpointServer(listener, endpoint, HttpStatusCode.OK, port));
        }

        public static Task<OpenSandboxEndpointServer> StartUnavailableAsync(HttpStatusCode responseStatusCode)
        {
            var tcpListener = new TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            return Task.FromResult(new OpenSandboxEndpointServer(listener, endpoint: null, responseStatusCode, port));
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            listener.Close();

            try
            {
                await processingTask;
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task ProcessAsync()
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    Requests.Add(context.Request.RawUrl ?? string.Empty);

                    if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                        context.Request.Url?.AbsolutePath.EndsWith("/sandboxes/sandbox-001", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.ContentType = "application/json";
                        await using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8, 1024, leaveOpen: true);
                        await writer.WriteAsync(JsonSerializer.Serialize(new
                        {
                            status = new
                            {
                                state = "Running"
                            }
                        }));
                    }
                    else if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                             context.Request.Url?.AbsolutePath.EndsWith("/sandboxes/sandbox-001/endpoints/18790", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        context.Response.StatusCode = (int)responseStatusCode;
                        if (responseStatusCode == HttpStatusCode.OK && endpoint is not null)
                        {
                            context.Response.ContentType = "application/json";
                            await using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8, 1024, leaveOpen: true);
                            await writer.WriteAsync(JsonSerializer.Serialize(new { endpoint }));
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    }

                    context.Response.Close();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
    }
}
