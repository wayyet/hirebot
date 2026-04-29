using System.IO.Compression;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Providers;
using HireBot.Core.Services.Evaluation;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class SandboxDelegationTests
{
    [Fact]
    public async Task EmployeeHiringService_ConversationApis_ShouldDelegateToSandboxService()
    {
        var sandboxService = new RecordingSandboxService
        {
            EnsureSessionResponse = ApiResponse<StartHiringConversationResultDto>.SuccessResponse(
                new StartHiringConversationResultDto("hire-001", "session-001", "goal", false, [])),
            TimelineResponse = ApiResponse<HiringConversationTimelineDto>.SuccessResponse(
                new HiringConversationTimelineDto(
                    "hire-001",
                    "session-001",
                    "goal",
                    false,
                    "in_progress",
                    [new HiringConversationMessageDto("msg-001", "assistant", "你好", DateTimeOffset.UtcNow)],
                    [])),
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-001",
                    "session-001",
                    "goal",
                    false,
                    new HiringConversationMessageDto("msg-002", "assistant", "收到", DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        "hire-001",
                        "goal",
                        "discovery",
                        "summary",
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow)))
        };

        var service = CreateEmployeeHiringService(sandboxService);

        var startResult = await service.StartConversationAsync("hire-001");
        Assert.True(startResult.Success);
        Assert.Equal("hire-001", sandboxService.LastEnsureSessionRequest!.ScopeKey);
        Assert.Equal(SandboxScopeTypes.Hire, sandboxService.LastEnsureSessionRequest.ScopeType);
        Assert.Equal("hiring", sandboxService.LastEnsureSessionRequest.SandboxRole);

        var sendResult = await service.SendConversationMessageAsync(
            "hire-001",
            new HiringConversationMessageRequestDto
            {
                Content = "请继续"
            });

        Assert.True(sendResult.Success);
        Assert.Equal("请继续", sandboxService.LastSendMessageRequest!.Content);
        Assert.Equal("hire-001", sandboxService.LastSendMessageRequest.ScopeKey);

        var timelineResult = await service.GetConversationTimelineAsync("hire-001");
        Assert.True(timelineResult.Success);
        Assert.Equal("hire-001", sandboxService.LastTimelineRequest!.ScopeKey);
        Assert.Equal("tenant-1:operator-1", sandboxService.LastTimelineRequest.OwnerSubject);
    }

    [Fact]
    public async Task EvaluationService_SandboxHelperMethods_ShouldDelegateToSandboxService()
    {
        var sandboxService = new RecordingSandboxService
        {
            EnsureSessionResponse = ApiResponse<StartHiringConversationResultDto>.SuccessResponse(
                new StartHiringConversationResultDto("hire-evaluator", "session-evaluator", "goal", false, [])),
            TimelineResponse = ApiResponse<HiringConversationTimelineDto>.SuccessResponse(
                new HiringConversationTimelineDto(
                    "hire-evaluator",
                    "session-evaluator",
                    "goal",
                    false,
                    "in_progress",
                    [],
                    [])),
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-evaluator",
                    "session-evaluator",
                    "goal",
                    false,
                    new HiringConversationMessageDto("msg-010", "assistant", "已处理", DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        "hire-evaluator",
                        "goal",
                        "evaluation",
                        "summary",
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow)))
        };

        var service = CreateEvaluationService(sandboxService);

        var startResult = await InvokePrivateAsync<ApiResponse<StartHiringConversationResultDto>>(
            service,
            "EnsureSandboxConversationStartedAsync",
            "tenant-2:operator-2",
            "hire-evaluator",
            "sandbox-evaluator",
            "evaluation-evaluator",
            CancellationToken.None);

        Assert.True(startResult.Success);
        Assert.Equal("tenant-2", sandboxService.LastEnsureSessionRequest!.TenantId);
        Assert.Equal("operator-2", sandboxService.LastEnsureSessionRequest.OperatorId);
        Assert.Equal("sandbox-evaluator", sandboxService.LastEnsureSessionRequest.SandboxId);

        var timelineResult = await InvokePrivateAsync<ApiResponse<HiringConversationTimelineDto>>(
            service,
            "GetSandboxTimelineAsync",
            "tenant-2:operator-2",
            "hire-evaluator",
            "sandbox-evaluator",
            "evaluation-evaluator",
            CancellationToken.None);

        Assert.True(timelineResult.Success);
        Assert.Equal("hire-evaluator", sandboxService.LastTimelineRequest!.ScopeKey);
        Assert.Equal("evaluation-evaluator", sandboxService.LastTimelineRequest.SandboxRole);

        var sendResult = await InvokePrivateAsync<ApiResponse<HiringConversationResultDto>>(
            service,
            "SendSandboxMessageAsync",
            "tenant-2:operator-2",
            "hire-evaluator",
            "sandbox-evaluator",
            "evaluation-evaluator",
            new HiringConversationMessageRequestDto
            {
                Content = "请评估当前回复"
            },
            CancellationToken.None);

        Assert.True(sendResult.Success);
        Assert.Equal("请评估当前回复", sandboxService.LastSendMessageRequest!.Content);
        Assert.Equal("tenant-2", sandboxService.LastSendMessageRequest.TenantId);
        Assert.Equal("operator-2", sandboxService.LastSendMessageRequest.OperatorId);
    }

    [Fact]
    public async Task EvaluationService_TestcaseLoading_ShouldReadFromHiringArtifactPackageService()
    {
        var artifactPackageService = new RecordingHiringArtifactPackageService
        {
            LatestPackage = new HiringArtifactPackageSnapshotDto(
                "hire-target",
                "session-target",
                HiringArtifactPackageKinds.IntermediatePackageZip,
                "hire-target_intermediate_package.zip",
                "packages/intermediate/package.zip",
                "sha256",
                BuildZipArchive(
                    new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["testcases/evaluation-test-cases.json"] = Encoding.UTF8.GetBytes("{\"test_cases\":[]}")
                    }),
                false)
        };

        var service = new EvaluationService(
            new InMemoryEmployeeRuntimeStore(),
            new NoopEmployeeHiringService(),
            artifactPackageService,
            new RecordingSandboxService(),
            new StubRequestContextService("tenant-2:operator-2"),
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new NoopEvaluationAssetStore(),
            new StubHostEnvironment(),
            new ConfigurationBuilder().Build(),
            NullLogger<EvaluationService>.Instance);

        var result = await InvokePrivateAsync<object>(
            service,
            "LoadTestcaseSourcesFromTargetArtifactsAsync",
            "hire-target",
            CancellationToken.None);

        var items = ((System.Collections.IEnumerable)result).Cast<object>().ToArray();
        var firstItem = Assert.Single(items);
        Assert.Equal("hire-target", artifactPackageService.LastHireId);
        Assert.Equal(
            "testcases/evaluation-test-cases.json",
            firstItem.GetType().GetProperty("SourcePath")!.GetValue(firstItem));
        Assert.Equal(
            HiringArtifactPackageKinds.IntermediatePackageZip,
            firstItem.GetType().GetProperty("SourceType")!.GetValue(firstItem));
    }

    private static EmployeeHiringService CreateEmployeeHiringService(RecordingSandboxService sandboxService)
    {
        return new EmployeeHiringService(
            new NoopTemplateDataProvider(),
            new NoopTemplatePackageProvider(),
            new NoopDiscoveryRuleProvider(),
            new NoopSystemSkillRegistry(),
            new HiringStageCompletionEvaluator(),
            new InMemoryHiringRuntimeStore(),
            new ThrowingKingCrabHttpClient(),
            sandboxService,
            new ConfigurationBuilder().Build(),
            new HttpContextAccessor
            {
                HttpContext = CreateHttpContext("tenant-1", "operator-1")
            },
            new SimpleServiceScopeFactory(),
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new NoopHiringFileStore(),
            new NoopHiringArtifactPackageService(),
            NullLogger<EmployeeHiringService>.Instance);
    }

    private static EvaluationService CreateEvaluationService(RecordingSandboxService sandboxService)
    {
        return new EvaluationService(
            new InMemoryEmployeeRuntimeStore(),
            new NoopEmployeeHiringService(),
            new NoopHiringArtifactPackageService(),
            sandboxService,
            new StubRequestContextService("tenant-2:operator-2"),
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new NoopEvaluationAssetStore(),
            new StubHostEnvironment(),
            new ConfigurationBuilder().Build(),
            NullLogger<EvaluationService>.Instance);
    }

    private static HireBotDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new HireBotDbContext(options);
    }

    private static DefaultHttpContext CreateHttpContext(string tenantId, string operatorId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", $"{tenantId}:{operatorId}"),
            new Claim("tenant_id", tenantId),
            new Claim("preferred_username", operatorId)
        ], "test"));
        return context;
    }

    private static async Task<TResult> InvokePrivateAsync<TResult>(object instance, string methodName, params object[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(instance, arguments) as Task;
        Assert.NotNull(task);

        await task!;
        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return (TResult)resultProperty!.GetValue(task)!;
    }

    private static byte[] BuildZipArchive(IReadOnlyDictionary<string, byte[]> files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key);
                using var stream = entry.Open();
                stream.Write(file.Value, 0, file.Value.Length);
            }
        }

        return memory.ToArray();
    }

    private sealed class RecordingSandboxService : ISandboxService
    {
        public SandboxEnsureSessionRequestDto? LastEnsureSessionRequest { get; private set; }

        public SandboxSendMessageRequestDto? LastSendMessageRequest { get; private set; }

        public SandboxTimelineRequestDto? LastTimelineRequest { get; private set; }

        public ApiResponse<StartHiringConversationResultDto> EnsureSessionResponse { get; init; } =
            ApiResponse<StartHiringConversationResultDto>.ErrorResponse(500, "not configured");

        public ApiResponse<HiringConversationResultDto> SendMessageResponse { get; init; } =
            ApiResponse<HiringConversationResultDto>.ErrorResponse(500, "not configured");

        public ApiResponse<HiringConversationTimelineDto> TimelineResponse { get; init; } =
            ApiResponse<HiringConversationTimelineDto>.ErrorResponse(500, "not configured");

        public Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(SandboxRegisterRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<SandboxInstanceDto>> CreateAsync(SandboxCreateRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxInstanceDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<bool>> DeleteAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<bool>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(SandboxEnsureSessionRequestDto request, CancellationToken cancellationToken = default)
        {
            LastEnsureSessionRequest = request;
            return Task.FromResult(EnsureSessionResponse);
        }

        public Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(SandboxSendMessageRequestDto request, CancellationToken cancellationToken = default)
        {
            LastSendMessageRequest = request;
            return Task.FromResult(SendMessageResponse);
        }

        public Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(SandboxTimelineRequestDto request, CancellationToken cancellationToken = default)
        {
            LastTimelineRequest = request;
            return Task.FromResult(TimelineResponse);
        }

        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxAttachmentUploadResultDto>.ErrorResponse(501, "not used"));
    }

    private sealed class ThrowingKingCrabHttpClient : IKingCrabHttpClient
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
            => throw new NotSupportedException("This test should route through ISandboxService instead of IKingCrabHttpClient.");

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
            => throw new NotSupportedException("This test should route through ISandboxService instead of IKingCrabHttpClient.");

        public Task<RemoteBinaryCallResult> SendForBinaryAsync(
            HttpMethod method,
            string path,
            object? body,
            string ownerSubject,
            CancellationToken cancellationToken,
            bool useHireBotApiPrefix = true,
            string? absoluteBaseUrl = null,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
            => throw new NotSupportedException("This test should route through ISandboxService instead of IKingCrabHttpClient.");
    }

    private sealed class NoopTemplateDataProvider : ITemplateDataProvider
    {
        public Task<IReadOnlyList<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopTemplatePackageProvider : ITemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopDiscoveryRuleProvider : IDiscoveryRuleProvider
    {
        public Task<DiscoverySkillDefinition> LoadAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopSystemSkillRegistry : ISystemSkillRegistry
    {
        public Task<IReadOnlyList<SystemSkillPackage>> ListAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SystemSkillPackage?> FindAsync(string skillId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SystemSkillPackage> LoadRequiredAsync(string skillId, string? configuredPath = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopHiringFileStore : IHiringFileStore
    {
        public Task<string> SaveAsync(string sessionId, string category, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopEmployeeHiringService : IEmployeeHiringService
    {
        public Task<ApiResponse<HireTemplateResultDto>> HireAsync(string templateId, HireTemplateRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HireTemplateResultDto>> CreateEvaluationWorkspaceAsync(string targetHireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<StartHiringConversationResultDto>> StartConversationAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringConversationControlResultDto>> PauseConversationAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringConversationControlResultDto>> ResumeConversationAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringConversationResultDto>> SendConversationMessageAsync(string hireId, HiringConversationMessageRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringConversationTimelineDto>> GetConversationTimelineAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(string hireId, string? stage, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(string hireId, HiringAuditDecisionRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringFinalizeResultDto>> FinalizeAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringWorkflowStateDto>> GetWorkflowStateAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<bool>> UploadEvaluationSkillAsync(string hireId, string? skillRootPath = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopHiringArtifactPackageService : IHiringArtifactPackageService
    {
        public Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingHiringArtifactPackageService : IHiringArtifactPackageService
    {
        public string? LastHireId { get; private set; }

        public HiringArtifactPackageSnapshotDto? LatestPackage { get; init; }

        public Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(string hireId, CancellationToken cancellationToken = default)
        {
            LastHireId = hireId;
            return Task.FromResult(LatestPackage);
        }

        public Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubRequestContextService(string ownerSubject) : IRequestContextService
    {
        public string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null) => ownerSubject;

        public (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
            => ("tenant-2", "operator-2");
    }

    private sealed class NoopEvaluationAssetStore : IEvaluationAssetStore
    {
        public Task<StoredEvaluationAsset> SaveTextAsync(string sessionId, int iteration, string assetType, string fileName, string content, string mimeType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StoredEvaluationAsset> SaveBytesAsync(string sessionId, int iteration, string assetType, string fileName, byte[] content, string mimeType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "HireBot.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class SimpleServiceScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new SimpleServiceScope();
    }

    private sealed class SimpleServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();

        public void Dispose()
        {
        }
    }
}
