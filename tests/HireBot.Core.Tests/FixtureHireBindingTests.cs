using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Core.Providers;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Core.Tests;

public sealed class FixtureHireBindingTests
{
    [Fact]
    public async Task HireFromFixtureTemplate_WithCanonicalUuid_ShouldSucceed_WhenFixtureIsInterningAi()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var requestContextService = new RequestContextService(httpContextAccessor);
        var service = new EmployeeRuntimeService(
            new InMemoryEmployeeRuntimeStore(),
            new InMemoryTeamImProvider(),
            new NoopCollaborationService(),
            requestContextService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new NoopInstanceArtifactCloneService());

        var response = await service.HireFromFixtureTemplateAsync("019dcfca-08a3-7a2a-bd14-09e790eab6f7");

        Assert.True(response.Success);
        Assert.Equal(200, response.Code);
        Assert.NotNull(response.Data);
        Assert.Equal("e_dev_seed_401_asset-guardian", response.Data!.EmployeeId);
        Assert.Equal("interning_ai", response.Data.Status);
    }

    [Fact]
    public async Task HireFromFixtureTemplate_WithCatalogAliasUuid_ShouldSucceed()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var requestContextService = new RequestContextService(httpContextAccessor);
        var service = new EmployeeRuntimeService(
            new InMemoryEmployeeRuntimeStore(),
            new InMemoryTeamImProvider(),
            new NoopCollaborationService(),
            requestContextService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new NoopInstanceArtifactCloneService());

        var response = await service.HireFromFixtureTemplateAsync("019ddd2a-4955-7acb-9930-67f88bf8ae8c");

        Assert.True(response.Success);
        Assert.Equal(200, response.Code);
        Assert.Equal("e_dev_seed_401_asset-guardian", response.Data!.EmployeeId);
    }

    [Fact]
    public async Task HireFromFixtureTemplate_WithUnknownTemplateId_ShouldReturn404()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var requestContextService = new RequestContextService(httpContextAccessor);
        var service = new EmployeeRuntimeService(
            new InMemoryEmployeeRuntimeStore(),
            new InMemoryTeamImProvider(),
            new NoopCollaborationService(),
            requestContextService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new NoopInstanceArtifactCloneService());

        var response = await service.HireFromFixtureTemplateAsync("nonexistent-template-id-00000000-0000-0000-0000-000000000000");

        Assert.False(response.Success);
        Assert.Equal(404, response.Code);
    }

    private static HireBotDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new HireBotDbContext(options);
    }

    private sealed class NoopCollaborationService : ICollaborationService
    {
        public Task<ApiResponse<IReadOnlyList<Abstraction.Models.Collaboration.CollaborationGroupSummaryDto>>> GetGroupsAsync(
            bool includeArchived,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<IReadOnlyList<Abstraction.Models.Collaboration.CollaborationGroupSummaryDto>>.SuccessResponse([]));
        }

        public Task<ApiResponse<Abstraction.Models.Collaboration.CollaborationGroupDetailDto>> GetGroupAsync(
            string groupId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ApiResponse<Abstraction.Models.Collaboration.CollaborationGroupDetailDto>> SetArchivedAsync(
            string groupId,
            bool archived,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> MarkArchivedAsync(IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class NoopInstanceArtifactCloneService : IInstanceArtifactCloneService
    {
        public Task<InstanceArtifactCloneResult> CloneArtifactsAsync(
            EmployeeDetailDto source,
            string targetInstanceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceArtifactCloneResult("current", "target", []));

        public Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(
            string departmentInstanceId,
            IReadOnlyDictionary<string, byte[]> files,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceArtifactCloneResult("current", "target", []));
    }
}
