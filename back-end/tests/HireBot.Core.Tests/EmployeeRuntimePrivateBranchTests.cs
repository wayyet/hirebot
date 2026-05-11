using HireBot.Abstraction;
using HireBot.Abstraction.Models.Collaboration;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Models.Team;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace HireBot.Core.Tests;

public sealed class EmployeeRuntimePrivateBranchTests
{
    [Fact]
    public async Task CreatePrivateBranchAsync_FromLivePersonalClone_CreatesHiredBranch()
    {
        await using var dbContext = CreateDbContext();
        var store = new MemoryStore();
        var source = BuildEmployee("pc_src", "personal_clone", "live", "owner-1", "dept_x");
        await store.UpsertAsync(source.OwnerUserId, source);
        var artifacts = new FakeArtifactCloneService();
        var service = CreateService(store, dbContext, artifacts);

        var response = await service.CreatePrivateBranchAsync(
            "pc_src",
            new CreatePrivateBranchRequestDto("我的私有分支", "调整人设和知识", ["persona", "knowledge"]));

        Assert.True(response.Success, response.Message);
        var branch = response.Data!;
        Assert.StartsWith("pb_", branch.BranchId, StringComparison.Ordinal);
        Assert.Equal("我的私有分支", branch.DisplayName);
        Assert.Equal("hired", branch.Status);
        Assert.Equal("pc_src", branch.FromInstanceId);
        Assert.False(branch.ImRoutingSwitched);
        Assert.Single(artifacts.CloneCalls);
        Assert.Equal("pc_src", artifacts.CloneCalls[0].Source.EmployeeId);

        var stored = await store.GetAsync("owner-1", branch.BranchId);
        Assert.NotNull(stored);
        Assert.Equal("private_branch", stored!.InstanceType);
        Assert.Equal("hired", stored.Status);

        var instance = await dbContext.Instances.SingleAsync(item => item.InstanceId == branch.BranchId);
        Assert.Equal("private_branch", instance.InstanceType);
        Assert.Equal("hired", instance.Status);
        Assert.Equal("pc_src", instance.FromInstanceId);
    }

    [Fact]
    public async Task CreatePrivateBranchAsync_FromDepartment_ReturnsConflict()
    {
        await using var dbContext = CreateDbContext();
        var store = new MemoryStore();
        await store.UpsertAsync("manager", BuildEmployee("dept_1", "department", "live", "manager", null));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.CreatePrivateBranchAsync(
            "dept_1",
            new CreatePrivateBranchRequestDto("分支", null, ["persona"]));

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
        Assert.Contains("个人分身", response.Message);
    }

    [Fact]
    public async Task CreatePrivateBranchAsync_FromPrivateBranch_ReturnsConflict()
    {
        await using var dbContext = CreateDbContext();
        var store = new MemoryStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pb_existing", "private_branch", "live", "owner-1", "pc_src"));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.CreatePrivateBranchAsync(
            "pb_existing",
            new CreatePrivateBranchRequestDto("二级分支", null, ["ability"]));

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
    }

    [Fact]
    public async Task CreatePrivateBranchAsync_WhenDuplicateName_ReturnsConflict()
    {
        await using var dbContext = CreateDbContext();
        var store = new MemoryStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pc_src", "personal_clone", "live", "owner-1", "dept_x"));
        await store.UpsertAsync("owner-1", BuildEmployee("existing_pb", "private_branch", "live", "owner-1", "pc_src") with
        {
            Nickname = "我的私有分支"
        });
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.CreatePrivateBranchAsync(
            "pc_src",
            new CreatePrivateBranchRequestDto("我的私有分支", null, ["persona"]));

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
    }

    [Fact]
    public async Task CreatePrivateBranchAsync_WhenSourceNotLive_ReturnsConflict()
    {
        await using var dbContext = CreateDbContext();
        var store = new MemoryStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pc_src", "personal_clone", "hired", "owner-1", "dept_x"));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.CreatePrivateBranchAsync(
            "pc_src",
            new CreatePrivateBranchRequestDto("分支", null, ["persona"]));

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
    }

    [Fact]
    public async Task CreatePrivateBranchAsync_WhenActiveBranchExists_ReturnsConflict()
    {
        await using var dbContext = CreateDbContext();
        // Seed an existing active private branch from the same source
        dbContext.Instances.Add(new Repository.Entities.InstanceEntity
        {
            InstanceId = "pb_active",
            TenantId = "tenant-default",
            InstanceType = "private_branch",
            Status = "live",
            BasedOnTemplateId = "tpl-sales",
            FromInstanceId = "pc_src",
            OwnerUserId = "owner-1",
            DepartmentId = "tenant-default",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var store = new MemoryStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pc_src", "personal_clone", "live", "owner-1", "dept_x"));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.CreatePrivateBranchAsync(
            "pc_src",
            new CreatePrivateBranchRequestDto("新分支", null, ["persona"]));

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
        Assert.Contains("活跃的私有分支", response.Message);
    }

    [Fact]
    public async Task AbandonPrivateBranchAsync_MarksAsRetiredAndCleansUp()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Instances.Add(new Repository.Entities.InstanceEntity
        {
            InstanceId = "pb_to_abandon",
            TenantId = "tenant-default",
            InstanceType = "private_branch",
            Status = "hired",
            BasedOnTemplateId = "tpl-sales",
            FromInstanceId = "pc_src",
            OwnerUserId = "owner-1",
            DepartmentId = "tenant-default",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var store = new MemoryStore();
        var branch = BuildEmployee("pb_to_abandon", "private_branch", "hired", "owner-1", "pc_src");
        await store.UpsertAsync("owner-1", branch);
        await store.UpsertAsync("owner-1", BuildEmployee("pc_src", "personal_clone", "live", "owner-1", "dept_x"));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.AbandonPrivateBranchAsync("pb_to_abandon");

        Assert.True(response.Success, response.Message);
        var instance = await dbContext.Instances.SingleAsync(item => item.InstanceId == "pb_to_abandon");
        Assert.Equal("retired", instance.Status);
        Assert.Null(instance.ActiveBranchId);
    }

    [Fact]
    public async Task AbandonPrivateBranchAsync_WhenLive_RestoresImRouting()
    {
        await using var dbContext = CreateDbContext();
        // Source clone with ActiveBranchId pointing to the branch
        dbContext.Instances.Add(new Repository.Entities.InstanceEntity
        {
            InstanceId = "pc_src",
            TenantId = "tenant-default",
            InstanceType = "personal_clone",
            Status = "live",
            BasedOnTemplateId = "tpl-sales",
            FromInstanceId = "dept_x",
            OwnerUserId = "owner-1",
            DepartmentId = "tenant-default",
            CurrentVersion = "v1",
            ActiveBranchId = "pb_live",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Instances.Add(new Repository.Entities.InstanceEntity
        {
            InstanceId = "pb_live",
            TenantId = "tenant-default",
            InstanceType = "private_branch",
            Status = "live",
            BasedOnTemplateId = "tpl-sales",
            FromInstanceId = "pc_src",
            OwnerUserId = "owner-1",
            DepartmentId = "tenant-default",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var store = new MemoryStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pb_live", "private_branch", "live", "owner-1", "pc_src"));
        await store.UpsertAsync("owner-1", BuildEmployee("pc_src", "personal_clone", "live", "owner-1", "dept_x"));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.AbandonPrivateBranchAsync("pb_live");

        Assert.True(response.Success, response.Message);
        // ActiveBranchId should be cleared on source
        var sourceEntity = await dbContext.Instances.SingleAsync(item => item.InstanceId == "pc_src");
        Assert.Null(sourceEntity.ActiveBranchId);
        // Branch should be retired
        var branchEntity = await dbContext.Instances.SingleAsync(item => item.InstanceId == "pb_live");
        Assert.Equal("retired", branchEntity.Status);
    }

    [Fact]
    public async Task UpdateLifecycleAsync_WhenPrivateBranchGoesLive_SetsActiveBranchId()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Instances.Add(new Repository.Entities.InstanceEntity
        {
            InstanceId = "pc_src",
            TenantId = "tenant-default",
            InstanceType = "personal_clone",
            Status = "live",
            BasedOnTemplateId = "tpl-sales",
            FromInstanceId = "dept_x",
            OwnerUserId = "owner-1",
            DepartmentId = "tenant-default",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Instances.Add(new Repository.Entities.InstanceEntity
        {
            InstanceId = "pb_eval",
            TenantId = "tenant-default",
            InstanceType = "private_branch",
            Status = "interning_human",
            BasedOnTemplateId = "tpl-sales",
            FromInstanceId = "pc_src",
            OwnerUserId = "owner-1",
            DepartmentId = "tenant-default",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var store = new MemoryStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pb_eval", "private_branch", "interning_human", "owner-1", "pc_src"));
        await store.UpsertAsync("owner-1", BuildEmployee("pc_src", "personal_clone", "live", "owner-1", "dept_x"));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.UpdateLifecycleAsync("pb_eval", new UpdateEmployeeLifecycleRequestDto
        {
            Status = "live"
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal("live", response.Data!.Status);

        // ActiveBranchId should be set on source
        var sourceEntity = await dbContext.Instances.SingleAsync(item => item.InstanceId == "pc_src");
        Assert.Equal("pb_eval", sourceEntity.ActiveBranchId);
    }

    [Fact]
    public async Task AbandonPrivateBranchAsync_WhenNotPrivateBranch_ReturnsConflict()
    {
        await using var dbContext = CreateDbContext();
        var store = new MemoryStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pc_src", "personal_clone", "live", "owner-1", "dept_x"));
        var service = CreateService(store, dbContext, new FakeArtifactCloneService());

        var response = await service.AbandonPrivateBranchAsync("pc_src");

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
    }

    private static EmployeeRuntimeService CreateService(
        IEmployeeRuntimeStore store,
        HireBotDbContext dbContext,
        IInstanceArtifactCloneService artifacts)
    {
        return new EmployeeRuntimeService(
            store,
            new FakeTeamImProvider(),
            new FakeCollaborationService(),
            new FakeRequestContextService("owner-1"),
            dbContext,
            artifacts,
            new NoopInstanceArtifactResolver(),
            new NoopSandboxService(),
            new NoopKingCrabHttpClient());
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private static EmployeeDetailDto BuildEmployee(
        string id,
        string type,
        string status,
        string owner,
        string? fromInstanceId)
    {
        return new EmployeeDetailDto(
            id,
            id,
            "销售助手",
            "销售模板",
            "tpl-sales",
            type,
            status,
            "tpl-sales",
            fromInstanceId,
            owner,
            "tenant-default",
            status,
            "summary",
            "ok",
            "ok",
            "tenant-default",
            "2026-04-29",
            null,
            null,
            0,
            0,
            null,
            [],
            [new EmployeeCapabilityDto("站内对话", true)],
            null,
            null,
            null,
            true);
    }

    private sealed class MemoryStore : IEmployeeRuntimeStore
    {
        private readonly Dictionary<string, Dictionary<string, EmployeeDetailDto>> employees = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<EmployeeDetailDto>> ListAsync(string ownerSubject, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<EmployeeDetailDto>>(
                employees.TryGetValue(ownerSubject, out var ownerEmployees) ? ownerEmployees.Values.ToArray() : []);
        }

        public Task<EmployeeDetailDto?> GetAsync(string ownerSubject, string employeeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                employees.TryGetValue(ownerSubject, out var ownerEmployees) &&
                ownerEmployees.TryGetValue(employeeId, out var employee)
                    ? employee
                    : null);
        }

        public Task<EmployeeDetailDto?> FindAsync(string employeeId, CancellationToken cancellationToken = default)
        {
            foreach (var ownerEmployees in employees.Values)
            {
                if (ownerEmployees.TryGetValue(employeeId, out var employee))
                    return Task.FromResult<EmployeeDetailDto?>(employee);
            }
            return Task.FromResult<EmployeeDetailDto?>(null);
        }

        public async Task<bool> ExistsNameAsync(string ownerSubject, string displayName, CancellationToken cancellationToken = default)
        {
            var list = await ListAsync(ownerSubject, cancellationToken);
            return list.Any(item =>
                (item.InstanceType == "personal_clone" || item.InstanceType == "private_branch") &&
                string.Equals(item.Nickname, displayName, StringComparison.OrdinalIgnoreCase));
        }

        public Task<EmployeeDetailDto> UpsertAsync(string ownerSubject, EmployeeDetailDto employee, CancellationToken cancellationToken = default)
        {
            if (!employees.TryGetValue(ownerSubject, out var ownerEmployees))
            {
                ownerEmployees = new Dictionary<string, EmployeeDetailDto>(StringComparer.OrdinalIgnoreCase);
                employees[ownerSubject] = ownerEmployees;
            }
            ownerEmployees[employee.EmployeeId] = employee;
            return Task.FromResult(employee);
        }

        public async Task<int> UpsertManyAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> items, CancellationToken cancellationToken = default)
        {
            foreach (var item in items) await UpsertAsync(ownerSubject, item, cancellationToken);
            return items.Count;
        }

        public Task<int> ReplaceOwnerAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> items, CancellationToken cancellationToken = default)
        {
            employees[ownerSubject] = items.ToDictionary(item => item.EmployeeId, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(items.Count);
        }
    }

    private sealed class FakeArtifactCloneService : IInstanceArtifactCloneService
    {
        public List<(EmployeeDetailDto Source, string TargetInstanceId)> CloneCalls { get; } = [];
        private readonly string artifactRoot = CreateArtifactRoot();

        public Task<InstanceArtifactCloneResult> CloneArtifactsAsync(EmployeeDetailDto source, string targetInstanceId, CancellationToken cancellationToken = default)
        {
            CloneCalls.Add((source, targetInstanceId));
            return Task.FromResult(new InstanceArtifactCloneResult("v_clone", artifactRoot, ["manifest.json"]));
        }

        public Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(string departmentInstanceId, IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static string CreateArtifactRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "hirebot-private-branch-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{}");
            return root;
        }
    }

    private sealed class NoopInstanceArtifactResolver : IInstanceArtifactResolver
    {
        public Task<InstanceArtifactResolution> ResolveAsync(InstanceEntity instance, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceArtifactResolution("/noop", new Dictionary<string, string?>()));
    }

    private sealed class FakeRequestContextService(string ownerSubject) : IRequestContextService
    {
        public string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null) => ownerSubject;
        public (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
            => ("tenant-default", operatorId ?? "operator-1");
    }

    private sealed class FakeTeamImProvider : ITeamImProvider
    {
        public Task<IReadOnlyList<TeamImItemDto>> GetItemsAsync(string ownerSubject, TeamImQueryDto query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TeamImItemDto>>([]);
        public Task<TeamImItemDto?> ConfirmItemAsync(string ownerSubject, string itemId, string? requestId, string actor, CancellationToken cancellationToken = default) => Task.FromResult<TeamImItemDto?>(null);
        public Task<int> ReplaceItemsAsync(string ownerSubject, IReadOnlyList<TeamImItemDto> items, CancellationToken cancellationToken = default) => Task.FromResult(items.Count);
    }

    private sealed class FakeCollaborationService : ICollaborationService
    {
        public Task<ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>> GetGroupsAsync(bool includeArchived, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<CollaborationGroupDetailDto>> GetGroupAsync(string groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<CollaborationGroupDetailDto>> SetArchivedAsync(string groupId, bool archived, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkArchivedAsync(IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopSandboxService : ISandboxService
    {
        public Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(SandboxRegisterRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(SuccessResponse(request));
        public Task<ApiResponse<SandboxInstanceDto>> CreateAsync(SandboxCreateRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(SuccessResponse(request));
        public Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(SuccessResponse(request));
        public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(SuccessResponse(request));
        public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(SuccessResponse(request));
        public Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(SuccessResponse(request));
        public Task<ApiResponse<bool>> DeleteAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(ApiResponse<bool>.SuccessResponse(true));
        public Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(SandboxEnsureSessionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(SandboxSendMessageRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(SandboxTimelineRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(SandboxSessionDetailRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SkillPackageUploadResultDto>> UploadSkillPackageAsync(SkillPackageUploadRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken = default) => Task.FromResult<SandboxInstanceDto?>(null);

        private static ApiResponse<SandboxInstanceDto> SuccessResponse(object request)
            => ApiResponse<SandboxInstanceDto>.SuccessResponse(
                new SandboxInstanceDto(
                    Guid.NewGuid(), "sbx_test", "hire", "instance:test", "runtime", "managed",
                    "owner-1", "tenant-default", "operator-1", "Running", "http://gateway.local",
                    null, null, "runtime-chat-for:test", null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    private sealed class NoopKingCrabHttpClient : IKingCrabHttpClient
    {
        public Task<RemoteCallResult<T>> SendForJsonAsync<T>(
            HttpMethod method, string path, object? body, string ownerSubject,
            CancellationToken cancellationToken, bool useHireBotApiPrefix = true,
            string? absoluteBaseUrl = null, IReadOnlyDictionary<string, string>? additionalHeaders = null)
        {
            if (path.Contains("override") && method == HttpMethod.Delete)
                return Task.FromResult(RemoteCallResult<T>.Ok(default!));
            return Task.FromResult(RemoteCallResult<T>.Ok(default!));
        }

        public Task<RemoteCallResult<T>> SendMultipartForJsonAsync<T>(
            string path, string formFieldName, string fileName, byte[] content, string contentType,
            string ownerSubject, CancellationToken cancellationToken, bool useHireBotApiPrefix = false,
            string? absoluteBaseUrl = null, IReadOnlyDictionary<string, string>? additionalHeaders = null)
            => Task.FromResult(BuildSuccess<T>());

        public Task<RemoteBinaryCallResult> SendForBinaryAsync(
            HttpMethod method, string path, object? body, string ownerSubject,
            CancellationToken cancellationToken, bool useHireBotApiPrefix = true,
            string? absoluteBaseUrl = null, IReadOnlyDictionary<string, string>? additionalHeaders = null)
            => throw new NotSupportedException();

        private static RemoteCallResult<T> BuildSuccess<T>()
        {
            if (typeof(T).Name == "DigitalEmployeeUploadResponse")
            {
                var data = (T)Activator.CreateInstance(typeof(T),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null, args: [true, null, "uploaded", 0, Array.Empty<string>(), 0], culture: null)!;
                return RemoteCallResult<T>.Ok(data);
            }
            return RemoteCallResult<T>.Ok(default!);
        }
    }
}
