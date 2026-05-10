using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Extensions;
using HireBot.Core.Providers;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HireBot.Repository;

namespace HireBot.Core.Tests;

public sealed class ConfigurationAndRuntimeGuardTests
{
    [Fact]
    public async Task BuildServiceTemplateDataProvider_GetAllAsync_ShouldThrow_WhenBaseUrlMissing()
    {
        var provider = new BuildServiceTemplateDataProvider(
            new FakeHttpClientFactory(new HttpClient()),
            new HttpContextAccessor(),
            new ConfigurationBuilder().Build(),
            NullLogger<BuildServiceTemplateDataProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAllAsync());
    }

    [Fact]
    public async Task EmployeeRuntimeService_GetEmployeesAsync_ShouldAutoSeedFixtureData()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var requestContextService = new RequestContextService(httpContextAccessor);
        var service = new EmployeeRuntimeService(
            new InMemoryEmployeeRuntimeStore(),
            new InMemoryTeamImProvider(),
            new NoopCollaborationService(),
            requestContextService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new NoopInstanceArtifactCloneService(),
            new NoopInstanceArtifactResolver(),
            new NoopSandboxService(),
            new NoopKingCrabHttpClient());

        var response = await service.GetEmployeesAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.NotEmpty(response.Data);
        Assert.Contains(response.Data, item => item.Status == "live");
    }

    [Fact]
    public void AddHireBotServices_ShouldRequireDefaultConnectionString()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://localhost:18789/"),
                new KeyValuePair<string, string?>("BuildService:BaseUrl", "http://localhost:1080/")
            ])
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddHireBotServices(configuration));
        Assert.Equal("ConnectionStrings:DefaultConnection is required.", exception.Message);
    }

    [Fact]
    public void AddHireBotServices_ShouldRegisterFailFastProviders()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:DefaultConnection", "Host=localhost;Port=5432;Database=HireBot;Username=postgres;Password=postgres;"),
                new KeyValuePair<string, string?>("KingCrab:BaseUrl", "http://localhost:18789/"),
                new KeyValuePair<string, string?>("BuildService:BaseUrl", "http://localhost:1080/")
            ])
            .Build();

        services.AddHireBotServices(configuration);

        AssertRegistration<IEvaluationScenarioProvider, UnavailableEvaluationScenarioProvider>(services);
        AssertRegistration<ICollaborationProvider, UnavailableCollaborationProvider>(services);
        AssertFactoryRegistration<ISkillCatalogProvider>(services);
        AssertRegistration<ITemplateDataProvider, BuildServiceTemplateDataProvider>(services);
        AssertRegistration<ISandboxService, SandboxService>(services);
    }

    [Fact]
    public void AddHireBotServices_ShouldAllowLegacyKingCrewBaseUrl()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:DefaultConnection", "Host=localhost;Port=5432;Database=HireBot;Username=postgres;Password=postgres;"),
                new KeyValuePair<string, string?>("KingCrew:BaseUrl", "http://localhost:18789/"),
                new KeyValuePair<string, string?>("BuildService:BaseUrl", "http://localhost:1080/")
            ])
            .Build();

        services.AddHireBotServices(configuration);

        AssertRegistration<ISandboxService, SandboxService>(services);
    }

    private static void AssertRegistration<TService, TImplementation>(IServiceCollection services)
    {
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(TService));
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }

    private static void AssertFactoryRegistration<TService>(IServiceCollection services)
    {
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(TService));
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    private static HireBotDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new HireBotDbContext(options);
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NoopCollaborationService : ICollaborationService
    {
        public Task<ApiResponse<IReadOnlyList<HireBot.Abstraction.Models.Collaboration.CollaborationGroupSummaryDto>>> GetGroupsAsync(
            bool includeArchived,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<IReadOnlyList<HireBot.Abstraction.Models.Collaboration.CollaborationGroupSummaryDto>>.SuccessResponse([]));
        }

        public Task<ApiResponse<HireBot.Abstraction.Models.Collaboration.CollaborationGroupDetailDto>> GetGroupAsync(
            string groupId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ApiResponse<HireBot.Abstraction.Models.Collaboration.CollaborationGroupDetailDto>> SetArchivedAsync(
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

    private sealed class NoopSandboxService : ISandboxService
    {
        public Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(SandboxRegisterRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> CreateAsync(SandboxCreateRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<bool>> DeleteAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(SandboxEnsureSessionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(SandboxSendMessageRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(SandboxTimelineRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(SandboxSessionDetailRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SkillPackageUploadResultDto>> UploadSkillPackageAsync(SkillPackageUploadRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken = default)
            => Task.FromResult<SandboxInstanceDto?>(null);
    }

    private sealed class NoopKingCrabHttpClient : IKingCrabHttpClient
    {
        public Task<RemoteCallResult<T>> SendForJsonAsync<T>(
            HttpMethod method,
            string path,
            object? body,
            string ownerSubject,
            CancellationToken cancellationToken,
            bool useHireBotApiPrefix = true,
            string? absoluteBaseUrl = null,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
            => throw new NotSupportedException();

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
            => throw new NotSupportedException();

        public Task<RemoteBinaryCallResult> SendForBinaryAsync(
            HttpMethod method,
            string path,
            object? body,
            string ownerSubject,
            CancellationToken cancellationToken,
            bool useHireBotApiPrefix = true,
            string? absoluteBaseUrl = null,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
            => throw new NotSupportedException();
    }
}
