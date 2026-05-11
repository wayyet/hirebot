using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class InstanceRuntimeConversationServiceTests : IDisposable
{
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), $"hirebot-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SendMessageAsync_ForLivePersonalClone_CallsSandboxAndPersistsMessages()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedLivePersonalClone(dbContext);
        SeedArtifacts(instance);
        var sandbox = new FakeSandboxService("Sandbox answer");
        var service = CreateService(dbContext, sandbox: sandbox);

        var response = await service.SendMessageAsync(instance.InstanceId, "inapp", "你好", instance.OwnerUserId);

        Assert.True(response.Success, response.Message);
        Assert.Equal("Sandbox answer", response.Data!.AssistantMessage.Content);
        Assert.Single(sandbox.CreateRequests);
        Assert.Single(sandbox.SendRequests);
        Assert.Equal($"instance:{instance.InstanceId}", sandbox.SendRequests[0].ScopeKey);
        Assert.Equal("runtime", sandbox.SendRequests[0].SandboxRole);
        Assert.Equal("inapp", sandbox.SendRequests[0].SessionKey);
        Assert.Equal("你好", sandbox.SendRequests[0].Content);
        Assert.NotNull(sandbox.SendRequests[0].Materials);
        Assert.Empty(sandbox.SendRequests[0].Materials!);
        Assert.False(sandbox.SendRequests[0].UploadMaterialsAsAttachments);

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
        var sandbox = new FakeSandboxService("ok");
        var service = CreateService(dbContext, sandbox: sandbox);

        var first = await service.SendMessageAsync(instance.InstanceId, "feishu", "hello", instance.OwnerUserId, externalMessageId: "msg-1", externalUserId: "open-1");
        var second = await service.SendMessageAsync(instance.InstanceId, "feishu", "hello again", instance.OwnerUserId, externalMessageId: "msg-1", externalUserId: "open-1");

        Assert.True(first.Success, first.Message);
        Assert.False(second.Success);
        Assert.Equal(409, second.Code);
        Assert.Single(sandbox.SendRequests);
    }

    [Fact]
    public async Task SendMessageAsync_UsesSandboxReply()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedLivePersonalClone(dbContext);
        SeedArtifacts(instance);
        var service = CreateService(dbContext, sandbox: new FakeSandboxService("runtime reply"));

        var response = await service.SendMessageAsync(instance.InstanceId, "inapp", "hello replay", instance.OwnerUserId);

        Assert.True(response.Success, response.Message);
        Assert.Equal("runtime reply", response.Data!.AssistantMessage.Content);
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
        ISandboxService? sandbox = null)
    {
        return new InstanceRuntimeConversationService(
            dbContext,
            new FakeEmployeeRuntimeStore(),
            new FakeRequestContextService("owner-1"),
            sandbox ?? new FakeSandboxService("assistant"),
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

    private sealed class FakeSandboxService(string reply) : ISandboxService
    {
        public List<SandboxCreateRequestDto> CreateRequests { get; } = [];
        public List<SandboxSendMessageRequestDto> SendRequests { get; } = [];

        public Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(SandboxRegisterRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.SuccessResponse(BuildInstance(request.SandboxId, request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, request.TenantId, request.OperatorId)));

        public Task<ApiResponse<SandboxInstanceDto>> CreateAsync(SandboxCreateRequestDto request, CancellationToken cancellationToken = default)
        {
            CreateRequests.Add(request);
            return Task.FromResult(ApiResponse<SandboxInstanceDto>.SuccessResponse(BuildInstance("sandbox-test", request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, request.TenantId, request.OperatorId)));
        }

        public Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.SuccessResponse(BuildInstance(request.SandboxId ?? "sandbox-test", request.ScopeType ?? SandboxScopeTypes.Hire, request.ScopeKey ?? "scope", request.SandboxRole ?? "runtime", request.OwnerSubject ?? "owner-1", "tenant-1", "operator-1")));

        public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => RefreshAsync(request, cancellationToken);

        public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => RefreshAsync(request, cancellationToken);

        public Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => RefreshAsync(request, cancellationToken);

        public Task<ApiResponse<bool>> DeleteAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<bool>.SuccessResponse(true));

        public Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(SandboxEnsureSessionRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<StartHiringConversationResultDto>.SuccessResponse(new StartHiringConversationResultDto(request.ScopeKey, "session-test", "runtime", false, [])));

        public Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(SandboxSendMessageRequestDto request, CancellationToken cancellationToken = default)
        {
            SendRequests.Add(request);
            var message = new HiringConversationMessageDto("assistant-test", "assistant", reply, DateTimeOffset.UtcNow);
            return Task.FromResult(ApiResponse<HiringConversationResultDto>.SuccessResponse(new HiringConversationResultDto(request.ScopeKey, "session-test", "runtime", false, message, new HiringStagePreviewDto(request.ScopeKey, "runtime", "runtime", reply, new Dictionary<string, string?>(), [], [], false, DateTimeOffset.UtcNow))));
        }

        public Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(SandboxTimelineRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringConversationTimelineDto>.SuccessResponse(new HiringConversationTimelineDto(request.ScopeKey, "session-test", "runtime", false, "IN_PROGRESS", [], [])));

        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxAttachmentUploadResultDto>.SuccessResponse(new SandboxAttachmentUploadResultDto(Guid.NewGuid(), null, null, "media-test", "http://media", request.Material.Name, request.Material.MimeType ?? "application/octet-stream", request.Material.Size ?? 0, request.Material.ContentHash, request.Material.Metadata?.TryGetValue("storagePath", out var path) == true ? path : null, "[media-test]", DateTimeOffset.UtcNow)));

        public Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(SandboxSessionDetailRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SkillPackageUploadResultDto>> UploadSkillPackageAsync(SkillPackageUploadRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SkillPackageUploadResultDto>.SuccessResponse(new SkillPackageUploadResultDto(true, null, 1)));

        public Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken = default)
            => Task.FromResult<SandboxInstanceDto?>(null);

        private static SandboxInstanceDto BuildInstance(string sandboxId, string scopeType, string scopeKey, string sandboxRole, string ownerSubject, string tenantId, string operatorId)
            => new(Guid.NewGuid(), sandboxId, scopeType, scopeKey, sandboxRole, "managed", ownerSubject, tenantId, operatorId, "Running", "http://localhost:18789", null, null, null, null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
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

