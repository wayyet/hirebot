using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
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
    public async Task KingCrabHttpClient_SendForJsonAsync_ShouldPreferIncomingAuthorizationHeader()
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

        var client = new KingCrabHttpClient(
            new StubHttpClientFactory(httpClient),
            new ConfigurationBuilder().Build(),
            httpContextAccessor,
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
        Assert.Equal("Bearer inbound-token", request.Authorization);
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

        var client = new KingCrabHttpClient(
            new StubHttpClientFactory(httpClient),
            configuration,
            new HttpContextAccessor(),
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
    public async Task KingCrabHttpClient_SendMultipartForJsonAsync_ShouldNormalizeSandboxProxyBaseUrlWithoutScheme()
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

        var client = new KingCrabHttpClient(
            new StubHttpClientFactory(httpClient),
            configuration,
            new HttpContextAccessor(),
            NullLogger<KingCrabHttpClient>.Instance);

        var result = await client.SendMultipartForJsonAsync<EchoResult>(
            "/admin/digital-employee/upload",
            "file",
            "skill.zip",
            [0x01, 0x02, 0x03],
            "application/zip",
            "tenant-a:operator-b",
            CancellationToken.None,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: "opensandbox-server.zyagi.cn:1080/sandboxes/sandbox-001/proxy/18789");

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("opensandbox-server.zyagi.cn", request.Host);
        Assert.Equal("/sandboxes/sandbox-001/proxy/18789/admin/digital-employee/upload", request.Path);
        Assert.Equal("sandbox-token", request.AuthorizationBearerToken);
    }

    [Fact]
    public async Task KingCrabHttpClient_SendMultipartForJsonAsync_ShouldPreferIncomingAuthorizationForOidcSandboxGateway()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(new EchoResult("ok")));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://kingcrab.local/")
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:AuthToken", "sandbox-token"),
                new KeyValuePair<string, string?>("OpenSandbox:KingCrab:OidcAuthority", "http://id.example.com/realms/test")
            ])
            .Build();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpContextAccessor.HttpContext.Request.Headers.Authorization = "Bearer inbound-token";

        var client = new KingCrabHttpClient(
            new StubHttpClientFactory(httpClient),
            configuration,
            httpContextAccessor,
            NullLogger<KingCrabHttpClient>.Instance);

        var result = await client.SendMultipartForJsonAsync<EchoResult>(
            "/admin/digital-employee/upload",
            "file",
            "skill.zip",
            [0x01, 0x02, 0x03],
            "application/zip",
            "tenant-a:operator-b",
            CancellationToken.None,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: "opensandbox-server.zyagi.cn:1080/sandboxes/sandbox-001/proxy/18789");

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer inbound-token", request.Authorization);
        Assert.Equal("inbound-token", request.AuthorizationBearerToken);
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
                    Content = "请看附件",
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
            Assert.Contains("[FILE_URL:/media/media-001]", secondHandler.LastChatCompletionContent!);

            var uploadRequest = Assert.Single(secondHandler.Requests, item => item.Path == "/media/upload");
            Assert.Equal("sandbox-gateway.local", uploadRequest.Host);
            Assert.Equal("sandbox-token", uploadRequest.AuthorizationBearerToken);

            var chatRequest = Assert.Single(secondHandler.Requests, item => item.Path == "/v1/chat/completions");
            Assert.Equal("sandbox-gateway.local", chatRequest.Host);
            Assert.Equal("sandbox-token", chatRequest.AuthorizationBearerToken);
            Assert.StartsWith("session-", chatRequest.SessionHeader);

            var persistedAsset = await secondContext.SandboxAssets.SingleAsync();
            Assert.Equal("media-001", persistedAsset.MediaId);
            Assert.Equal("/media/media-001", persistedAsset.Url);
            Assert.NotNull(persistedAsset.SandboxInstanceEntityId);
            Assert.NotNull(persistedAsset.SandboxSessionEntityId);
        }
    }

    [Fact]
    public async Task SandboxService_ConversationLifecycle_ShouldStartSendAndReadTimeline()
    {
        const string databaseName = "sandbox-service-conversation-lifecycle";
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
                State = "Running",
                GatewayEndpoint = "http://sandbox-gateway.local/"
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
                Content = "请继续"
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

        Assert.True(timelineResult.Success);
        Assert.Equal(sessionId, timelineResult.Data!.SessionId);
        Assert.Equal(2, timelineResult.Data.Messages.Count);
        Assert.Equal("user", timelineResult.Data.Messages[0].Role);
        Assert.Equal("assistant", timelineResult.Data.Messages[1].Role);

        var chatRequest = Assert.Single(
            handler.Requests,
            request => request.Method == HttpMethod.Post &&
                       request.Path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sandbox-gateway.local", chatRequest.Host);
        Assert.Equal("sandbox-token", chatRequest.AuthorizationBearerToken);
        Assert.Equal(sessionId, chatRequest.SessionHeader);

        var historyRequest = Assert.Single(
            handler.Requests,
            request => request.Method == HttpMethod.Get &&
                       request.Path.Equals($"/api/integration/sessions/{sessionId}", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sandbox-gateway.local", historyRequest.Host);
        Assert.Equal("sandbox-token", historyRequest.AuthorizationBearerToken);

        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains("/conversation/", StringComparison.OrdinalIgnoreCase));
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

        var kingCrabHttpClient = new KingCrabHttpClient(
            new StubHttpClientFactory(new HttpClient(handler)
            {
                BaseAddress = new Uri("http://kingcrab.local/")
            }),
            configuration,
            httpContextAccessor,
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
        if (request.Path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
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
                            content = "收到"
                        }
                    }
                }
            });
        }

        if (request.Path.StartsWith("/api/integration/sessions/", StringComparison.OrdinalIgnoreCase) &&
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
                            content = "请继续",
                            timestamp = DateTimeOffset.UtcNow
                        },
                        new
                        {
                            role = "assistant",
                            content = "收到",
                            timestamp = DateTimeOffset.UtcNow
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
                new HiringConversationMessageDto("msg-001", "assistant", "收到", DateTimeOffset.UtcNow),
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

        if (request.Path.Equals("/media/upload", StringComparison.OrdinalIgnoreCase))
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

    private sealed record EchoResult(string Value);

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
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
                request.RequestUri.AbsolutePath.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase) &&
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
}
