using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace HireBot.Core.Tests;

public sealed class InstanceRuntimeConversationServiceTests : IDisposable
{
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), $"hirebot-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SendMessageAsync_ForLivePersonalClone_CallsKingCrewAndPersistsMessages()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedLivePersonalClone(dbContext);
        SeedArtifacts(instance);
        var kingCrew = new FakeKingCrewRuntimeChatClient("KingCrew answer");
        var service = CreateService(dbContext, kingCrew: kingCrew);

        var response = await service.SendMessageAsync(instance.InstanceId, "inapp", "你好", instance.OwnerUserId);

        Assert.True(response.Success, response.Message);
        Assert.Equal("KingCrew answer", response.Data!.AssistantMessage.Content);
        Assert.Single(kingCrew.Requests);
        Assert.Equal(instance.InstanceId, kingCrew.Requests[0].InstanceId);
        Assert.Equal("inapp", kingCrew.Requests[0].Channel);
        Assert.Equal(Path.Combine(artifactRoot, "instances", "personal_clone", instance.FromInstanceId!, instance.InstanceId, "versions", instance.CurrentVersion), kingCrew.Requests[0].ArtifactRoot);

        var messages = await dbContext.Messages.OrderBy(item => item.CreatedAt).ToArrayAsync();
        Assert.Equal(2, messages.Length);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("assistant", messages[1].Role);
        Assert.All(messages, message => Assert.Equal("inapp", message.Channel));
    }

    [Fact]
    public async Task SendMessageAsync_ForDuplicateExternalMessage_ReturnsConflictAndDoesNotCallKingCrewTwice()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedLivePersonalClone(dbContext);
        SeedArtifacts(instance);
        var kingCrew = new FakeKingCrewRuntimeChatClient("ok");
        var service = CreateService(dbContext, kingCrew: kingCrew);

        var first = await service.SendMessageAsync(instance.InstanceId, "feishu", "hello", instance.OwnerUserId, externalMessageId: "msg-1", externalUserId: "open-1");
        var second = await service.SendMessageAsync(instance.InstanceId, "feishu", "hello again", instance.OwnerUserId, externalMessageId: "msg-1", externalUserId: "open-1");

        Assert.True(first.Success, first.Message);
        Assert.False(second.Success);
        Assert.Equal(409, second.Code);
        Assert.Single(kingCrew.Requests);
    }

    [Fact]
    public async Task SendMessageAsync_WhenReplayContextUsesMockKingCrew_ReturnsMockReply()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedLivePersonalClone(dbContext);
        SeedArtifacts(instance);

        var replayContext = new FakeReplayContext
        {
            UseMockKingCrew = true,
            MockKingCrewReply = "mock reply: {last_user_message}"
        };

        var service = CreateService(
            dbContext,
            kingCrew: new KingCrewRuntimeChatClient(
                new StubHttpClientFactory(),
                CreateConfiguration(),
                replayContext,
                NullLogger<KingCrewRuntimeChatClient>.Instance));

        var response = await service.SendMessageAsync(instance.InstanceId, "inapp", "hello replay", instance.OwnerUserId);

        Assert.True(response.Success, response.Message);
        Assert.Equal("mock reply: hello replay", response.Data!.AssistantMessage.Content);
    }

    [Fact]
    public async Task SendMessageAsync_ForDepartmentInstance_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "dept_1", "department", "live", "owner-1", null);
        var service = CreateService(dbContext);

        var response = await service.SendMessageAsync(instance.InstanceId, "inapp", "你好", instance.OwnerUserId);

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
    }

    [Fact]
    public async Task ClearMessagesAsync_ClearsOnlyRequestedChannel()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedLivePersonalClone(dbContext);
        SeedArtifacts(instance);
        var service = CreateService(dbContext);

        await service.SendMessageAsync(instance.InstanceId, "inapp", "inapp", instance.OwnerUserId);
        await service.SendMessageAsync(instance.InstanceId, "feishu", "feishu", instance.OwnerUserId, externalMessageId: "feishu-1");

        var clear = await service.ClearMessagesAsync(instance.InstanceId, "inapp", instance.OwnerUserId);

        Assert.True(clear.Success, clear.Message);
        var remaining = await dbContext.Messages.ToArrayAsync();
        Assert.All(remaining, message => Assert.Equal("feishu", message.Channel));
    }

    private InstanceRuntimeConversationService CreateService(
        HireBotDbContext dbContext,
        IKingCrewRuntimeChatClient? kingCrew = null)
    {
        return new InstanceRuntimeConversationService(
            dbContext,
            new FakeEmployeeRuntimeStore(),
            new FakeRequestContextService("owner-1"),
            new InstanceArtifactResolver(CreateConfiguration()),
            kingCrew ?? new FakeKingCrewRuntimeChatClient("assistant"),
            NullLogger<InstanceRuntimeConversationService>.Instance);
    }

    private HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HireBot:ArtifactStoreRoot"] = artifactRoot
            })
            .Build();
    }

    private InstanceEntity SeedLivePersonalClone(HireBotDbContext dbContext)
    {
        return SeedInstance(dbContext, "pc_1", "personal_clone", "live", "owner-1", "dept_1");
    }

    private static InstanceEntity SeedInstance(
        HireBotDbContext dbContext,
        string instanceId,
        string instanceType,
        string status,
        string owner,
        string? fromInstanceId)
    {
        var instance = new InstanceEntity
        {
            InstanceId = instanceId,
            TenantId = "tenant-1",
            InstanceType = instanceType,
            Status = status,
            ViaQuickClone = false,
            BasedOnTemplateId = "tpl-1",
            FromInstanceId = fromInstanceId,
            EvalReportId = null,
            OwnerUserId = owner,
            DepartmentId = "dept",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Instances.Add(instance);
        dbContext.SaveChanges();
        return instance;
    }

    private void SeedArtifacts(InstanceEntity instance)
    {
        var root = Path.Combine(artifactRoot, "instances", "personal_clone", instance.FromInstanceId!, instance.InstanceId, "versions", instance.CurrentVersion);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "manifest.json"), "{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(artifactRoot))
        {
            Directory.Delete(artifactRoot, recursive: true);
        }
    }

    private sealed class FakeKingCrewRuntimeChatClient(string reply) : IKingCrewRuntimeChatClient
    {
        public List<RuntimeChatRequestDto> Requests { get; } = [];

        public Task<ApiResponse<RuntimeChatResponseDto>> SendAsync(
            RuntimeChatRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(ApiResponse<RuntimeChatResponseDto>.SuccessResponse(new RuntimeChatResponseDto(reply)));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new HttpClientHandler(), disposeHandler: false)
            {
                BaseAddress = new Uri("https://open.feishu.cn")
            };
        }
    }

    private sealed class FakeReplayContext : IImWebhookReplayContext
    {
        public bool SkipOutboundSend { get; set; }

        public bool UseMockKingCrew { get; set; }

        public string? MockKingCrewReply { get; set; }

        public void Reset()
        {
            SkipOutboundSend = false;
            UseMockKingCrew = false;
            MockKingCrewReply = null;
        }
    }

    private sealed class FakeRequestContextService(string ownerSubject) : IRequestContextService
    {
        public string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null) => ownerSubject;

        public (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
        {
            return (tenantId ?? "tenant-1", operatorId ?? "operator-1");
        }
    }

    private sealed class FakeEmployeeRuntimeStore : IEmployeeRuntimeStore
    {
        public Task<IReadOnlyList<EmployeeDetailDto>> ListAsync(string ownerSubject, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EmployeeDetailDto>>([]);

        public Task<EmployeeDetailDto?> GetAsync(string ownerSubject, string employeeId, CancellationToken cancellationToken = default) => Task.FromResult<EmployeeDetailDto?>(null);

        public Task<EmployeeDetailDto?> FindAsync(string employeeId, CancellationToken cancellationToken = default) => Task.FromResult<EmployeeDetailDto?>(null);

        public Task<bool> ExistsNameAsync(string ownerSubject, string displayName, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<EmployeeDetailDto> UpsertAsync(string ownerSubject, EmployeeDetailDto employee, CancellationToken cancellationToken = default) => Task.FromResult(employee);

        public Task<int> UpsertManyAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default) => Task.FromResult(employees.Count);

        public Task<int> ReplaceOwnerAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default) => Task.FromResult(employees.Count);
    }
}

