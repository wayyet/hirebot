using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class InstanceChatServiceTests
{
    [Fact]
    public async Task ClearFeishuChannelOverrideAsync_ShouldCallKingCrabDeleteOverrideEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(_ => JsonResponse(new
        {
            success = true,
            message = "Channel 'feishu' override cleared; reverted to appsettings.",
            error = (string?)null,
            mode = (string?)null
        }));

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

        using var dbContext = CreateDbContext();
        dbContext.Instances.Add(new InstanceEntity
        {
            InstanceId = "pc_1",
            TenantId = "tenant-1",
            InstanceType = "personal_clone",
            Status = "live",
            OwnerUserId = "owner-1",
            DepartmentId = "dept-1",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpContextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "owner-1")
        ], "test"));

        var service = new InstanceChatService(
            new KingCrabHttpClient(
                new StubHttpClientFactory(httpClient),
                configuration,
                new KingCrabSandboxTokenProvider(
                    new StubHttpClientFactory(httpClient),
                    configuration,
                    NullLogger<KingCrabSandboxTokenProvider>.Instance),
                NullLogger<KingCrabHttpClient>.Instance),
            dbContext,
            new TestUserIdentity("owner-1", "tenant-1", "operator-1"));

        var response = await service.ClearFeishuChannelOverrideAsync("pc_1");

        Assert.True(response.Success);
        Assert.Equal("Channel 'feishu' override cleared; reverted to appsettings.", response.Message);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/admin/channels/feishu/override", request.Path);
        Assert.Equal("Bearer sandbox-token", request.Authorization);
        Assert.Equal("owner-1", request.OwnerHeader);
    }

    [Fact]
    public async Task UpdateDingTalkChannelConfigAsync_ShouldCallKingCrabUpdateEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(_ => JsonResponse(new
        {
            success = true,
            message = "DingTalk config persisted.",
            error = (string?)null,
            mode = (string?)null
        }));

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

        using var dbContext = CreateDbContext();
        dbContext.Instances.Add(new InstanceEntity
        {
            InstanceId = "pc_1",
            TenantId = "tenant-1",
            InstanceType = "personal_clone",
            Status = "live",
            OwnerUserId = "owner-1",
            DepartmentId = "dept-1",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpContextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "owner-1")
        ], "test"));

        var service = new InstanceChatService(
            new KingCrabHttpClient(
                new StubHttpClientFactory(httpClient),
                configuration,
                new KingCrabSandboxTokenProvider(
                    new StubHttpClientFactory(httpClient),
                    configuration,
                    NullLogger<KingCrabSandboxTokenProvider>.Instance),
                NullLogger<KingCrabHttpClient>.Instance),
            dbContext,
            new TestUserIdentity("owner-1", "tenant-1", "operator-1"));

        var response = await service.UpdateDingTalkChannelConfigAsync("pc_1", new HireBot.Abstraction.Models.EmployeeRuntime.DingTalkChannelConfig
        {
            Enabled = true,
            AppId = "ding-app",
            AppKey = "ding-key",
            AppSecret = "ding-secret"
           
        });

        Assert.True(response.Success);
        Assert.Equal("DingTalk config persisted.", response.Data!.Message);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/admin/channels/dingtalk/update", request.Path);
        Assert.Equal("Bearer sandbox-token", request.Authorization);
        Assert.Equal("owner-1", request.OwnerHeader);
        Assert.Contains("\"appId\":\"ding-app\"", request.Body);
        Assert.Contains("\"appKey\":\"ding-key\"", request.Body);
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase("instance-chat-service-" + Guid.NewGuid())
            .UseInternalServiceProvider(new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider())
            .Options;

        return new HireBotDbContext(options);
    }

    private static HttpResponseMessage JsonResponse(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CapturingHttpMessageHandler(Func<CapturedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null)
            {
                body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }

            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-HireBot-Owner", out var owners) ? owners.FirstOrDefault() : null,
                body);

            Requests.Add(captured);
            return Task.FromResult(responder(captured));
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string? Authorization,
        string? OwnerHeader,
        string? Body);

    private sealed class TestUserIdentity(string ownerSubject, string tenantId, string operatorId) : HireBot.Abstraction.Infrastructure.Identity.IUserIdentity
    {
        public string Id => ownerSubject;
        public string Email => "test@example.com";
        public string UserName => "testuser";
        public string FirstName => "Test";
        public string LastName => "User";
        public string FullName => "Test User";
        public string DisplayName => "Test User";
        public string? TenantId => tenantId;
        public string? TenantName => "Test Tenant";
        public string OperatorId => operatorId;
        public string OwnerSubject => ownerSubject;
        public string? Role => "admin";
        public bool IsAuthenticated => true;
        public string? DepartmentId => null;
    }
}
