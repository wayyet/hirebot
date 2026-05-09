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
using System.Net.Http;

namespace HireBot.Core.Tests;

public sealed class EmployeeRuntimeRetirementCleanupTests
{
    [Fact]
    public async Task UpdateLifecycleAsync_RetiresOnlyTargetInstanceAndCleansItsArtifacts()
    {
        await using var dbContext = CreateDbContext();
        var store = new MemoryEmployeeRuntimeStore();
        await store.UpsertAsync("owner-1", BuildEmployee("pc_1", "live"));
        await store.UpsertAsync("owner-1", BuildEmployee("pc_2", "live"));

        var sandbox = new RecordingSandboxService();
        var kingCrab = new RecordingKingCrabHttpClient();
        var service = CreateService(store, dbContext, sandbox, kingCrab);

        var response = await service.UpdateLifecycleAsync(
            "pc_1",
            new UpdateEmployeeLifecycleRequestDto
            {
                Status = "retired"
            });

        Assert.True(response.Success, response.Message);
        Assert.Equal("retired", response.Data!.Status);
        Assert.Equal("retired", (await store.GetAsync("owner-1", "pc_1"))!.Status);
        Assert.Equal("live", (await store.GetAsync("owner-1", "pc_2"))!.Status);

        Assert.Single(sandbox.DeleteRequests);
        Assert.Equal("owner-1", sandbox.DeleteRequests[0].OwnerSubject);
        Assert.Equal(SandboxScopeTypes.Hire, sandbox.DeleteRequests[0].ScopeType);
        Assert.Equal("instance:pc_1", sandbox.DeleteRequests[0].ScopeKey);
        Assert.Equal("runtime", sandbox.DeleteRequests[0].SandboxRole);

        Assert.Equal(3, kingCrab.Calls.Count);
        Assert.Contains(kingCrab.Calls, call => call.Method == "DELETE" && call.Path == "/admin/channels/feishu/override");
        Assert.Contains(kingCrab.Calls, call => call.Method == "DELETE" && call.Path == "/admin/channels/dingtalk/override");
        Assert.Contains(kingCrab.Calls, call => call.Method == "DELETE" && call.Path == "/admin/channels/wecom/override");
    }

    private static EmployeeRuntimeService CreateService(
        IEmployeeRuntimeStore store,
        HireBotDbContext dbContext,
        ISandboxService sandboxService,
        IKingCrabHttpClient kingCrabHttpClient)
    {
        return new EmployeeRuntimeService(
            store,
            new FakeTeamImProvider(),
            new FakeCollaborationService(),
            new FakeRequestContextService("owner-1"),
            dbContext,
            new NoopArtifactCloneService(),
            sandboxService,
            kingCrabHttpClient);
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private static EmployeeDetailDto BuildEmployee(string employeeId, string status)
    {
        return new EmployeeDetailDto(
            employeeId,
            employeeId,
            "销售助手",
            "销售模板",
            "tpl-sales",
            "personal_clone",
            status,
            "tpl-sales",
            "dept_1",
            "owner-1",
            "tenant-default",
            status,
            "summary",
            "signal",
            "level",
            "tenant-default",
            "2026-05-08",
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

    private sealed class MemoryEmployeeRuntimeStore : IEmployeeRuntimeStore
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
                {
                    return Task.FromResult<EmployeeDetailDto?>(employee);
                }
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

        public async Task<int> UpsertManyAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default)
        {
            foreach (var employee in employees)
            {
                await UpsertAsync(ownerSubject, employee, cancellationToken);
            }

            return employees.Count;
        }

        public Task<int> ReplaceOwnerAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default)
        {
            this.employees[ownerSubject] = employees.ToDictionary(item => item.EmployeeId, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(employees.Count);
        }
    }

    private sealed class FakeRequestContextService(string ownerSubject) : IRequestContextService
    {
        public string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null) => ownerSubject;

        public (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
        {
            return ("tenant-default", "operator-1");
        }
    }

    private sealed class FakeTeamImProvider : ITeamImProvider
    {
        public Task<IReadOnlyList<TeamImItemDto>> GetItemsAsync(string ownerSubject, TeamImQueryDto query, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TeamImItemDto>>([]);

        public Task<TeamImItemDto?> ConfirmItemAsync(string ownerSubject, string itemId, string? requestId, string actor, CancellationToken cancellationToken = default)
            => Task.FromResult<TeamImItemDto?>(null);

        public Task<int> ReplaceItemsAsync(string ownerSubject, IReadOnlyList<TeamImItemDto> items, CancellationToken cancellationToken = default)
            => Task.FromResult(items.Count);
    }

    private sealed class FakeCollaborationService : ICollaborationService
    {
        public Task<ApiResponse<IReadOnlyList<CollaborationGroupSummaryDto>>> GetGroupsAsync(bool includeArchived, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<CollaborationGroupDetailDto>> GetGroupAsync(string groupId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<CollaborationGroupDetailDto>> SetArchivedAsync(string groupId, bool archived, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> MarkArchivedAsync(IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopArtifactCloneService : IInstanceArtifactCloneService
    {
        public Task<InstanceArtifactCloneResult> CloneArtifactsAsync(EmployeeDetailDto source, string targetInstanceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(string departmentInstanceId, IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingSandboxService : ISandboxService
    {
        public List<SandboxInstanceLookupRequestDto> DeleteRequests { get; } = [];

        public Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(SandboxRegisterRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SandboxInstanceDto>> CreateAsync(SandboxCreateRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<bool>> DeleteAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
        {
            DeleteRequests.Add(request);
            return Task.FromResult(ApiResponse<bool>.SuccessResponse(true));
        }

        public Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(SandboxEnsureSessionRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(SandboxSendMessageRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(SandboxTimelineRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(SandboxSessionDetailRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<SkillPackageUploadResultDto>> UploadSkillPackageAsync(SkillPackageUploadRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken = default)
            => Task.FromResult<SandboxInstanceDto?>(null);
    }

    private sealed class RecordingKingCrabHttpClient : IKingCrabHttpClient
    {
        public List<(string Method, string Path, string OwnerSubject)> Calls { get; } = [];

        public Task<RemoteCallResult<T>> SendForJsonAsync<T>(
            HttpMethod method,
            string path,
            object? body,
            string ownerSubject,
            CancellationToken cancellationToken,
            bool useHireBotApiPrefix = true,
            string? absoluteBaseUrl = null,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
        {
            Calls.Add((method.Method.ToUpperInvariant(), path, ownerSubject));

            return Task.FromResult(RemoteCallResult<T>.Ok(default!));
        }

        public Task<RemoteCallResult<T>> SendMultipartForJsonAsync<T>(
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
            => Task.FromResult(RemoteCallResult<T>.Ok(default!));

        public Task<RemoteBinaryCallResult> SendForBinaryAsync(
            HttpMethod method,
            string path,
            object? body,
            string ownerSubject,
            CancellationToken cancellationToken,
            bool useHireBotApiPrefix = true,
            string? absoluteBaseUrl = null,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
            => Task.FromResult(RemoteBinaryCallResult.Ok(Array.Empty<byte>(), null, null));
    }
}
