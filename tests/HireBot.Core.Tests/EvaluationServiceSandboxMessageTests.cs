using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Models.Training;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Evaluation;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class EvaluationServiceSandboxMessageTests
{
    [Fact]
    public async Task SendEvaluationSandboxMessageAsync_ShouldRun_AndRouteMessageThroughSandbox()
    {
        var owner = "tenant-test:operator-test";
        var employeeId = "emp-eval-1";
        var dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        var store = new RecordingEmployeeRuntimeStore(
            new EmployeeDetailDto(
                employeeId,
                "Eval Bot",
                "Eval Bot",
                "fixture-template",
                "fixture-template-id",
                "personal_clone",
                "live",
                null,
                null,
                owner,
                "dept-1",
                "live",
                "ready",
                "ok",
                "ok",
                "team-1",
                DateTimeOffset.UtcNow.ToString("o"),
                null,
                null,
                0,
                0,
                null,
                [],
                [],
                null,
                null,
                null,
                true));

        var hiring = new RecordingEmployeeHiringService
        {
            HireResponse = ApiResponse<HireTemplateResultDto>.SuccessResponse(
                new HireTemplateResultDto("hire-target-1", "sandbox-target-1", "running", "ready")),
            StatusResponse = ApiResponse<HiringStatusDto>.SuccessResponse(
                new HiringStatusDto("hire-target-1", "sandbox-target-1", "running", null, null, null, null)),
            CreateWorkspaceResponse = ApiResponse<HireTemplateResultDto>.SuccessResponse(
                new HireTemplateResultDto("hire-evaluator-1", "sandbox-evaluator-1", "running", "chat")),
            UploadSkillResponse = ApiResponse<bool>.SuccessResponse(true)
        };

        var sandbox = new RecordingSandboxService
        {
            EnsureSessionResponse = ApiResponse<StartHiringConversationResultDto>.SuccessResponse(
                new StartHiringConversationResultDto("hire-evaluator-1", "session-1", "chat", false, [])),
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-evaluator-1",
                    "session-1",
                    "chat",
                    false,
                    new HiringConversationMessageDto("msg-1", "assistant", "sandbox reply", DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto("hire-evaluator-1", "chat", "evaluation-evaluator", "preview", new Dictionary<string, string?>(), [], [], false, DateTimeOffset.UtcNow))),
            TimelineResponse = ApiResponse<HiringConversationTimelineDto>.SuccessResponse(
                new HiringConversationTimelineDto(
                    "hire-evaluator-1",
                    "session-1",
                    "chat",
                    false,
                    "in_progress",
                    [new HiringConversationMessageDto("msg-1", "assistant", "sandbox reply", DateTimeOffset.UtcNow)],
                    []))
        };

        var service = new EvaluationService(
            store,
            hiring,
            new NoopHiringArtifactPackageService(),
            sandbox,
            new StubRequestContextService(owner),
            dbContext,
            new NoopEvaluationAssetStore(),
            new StubHostEnvironment(),
            new ConfigurationBuilder().Build(),
            NullLogger<EvaluationService>.Instance);

        var response = await service.SendEvaluationSandboxMessageAsync(
            employeeId,
            new EvaluationSandboxMessageRequestDto
            {
                Content = "  hello evaluation sandbox  "
            });

        Assert.True(response.Success, response.Message);
        Assert.NotNull(response.Data);
        Assert.Equal("evaluation sandbox replied", response.Message);
        Assert.Equal("session-1", response.Data!.SessionId);
        Assert.Equal("hello evaluation sandbox", sandbox.LastSendMessageRequest!.Content);
        Assert.Equal("hire-evaluator-1", sandbox.LastSendMessageRequest.ScopeKey);
        Assert.Equal("evaluation-evaluator", sandbox.LastSendMessageRequest.SandboxRole);
        Assert.Equal("default", sandbox.LastEnsureSessionRequest!.SessionKey);
        Assert.Equal("sandbox-evaluator-1", sandbox.LastEnsureSessionRequest.SandboxId);
        Assert.Equal("hire-target-1", hiring.LastGetHiringStatusHireId);
        Assert.Equal("hire-target-1", hiring.LastCreateWorkspaceTargetHireId);
        Assert.Equal("hire-evaluator-1", hiring.LastUploadSkillHireId);
    }

    private static HireBotDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new HireBotDbContext(options);
    }

    private sealed class RecordingEmployeeRuntimeStore(EmployeeDetailDto employee) : IEmployeeRuntimeStore
    {
        public Task<IReadOnlyList<EmployeeDetailDto>> ListAsync(string ownerSubject, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmployeeDetailDto>>([employee]);

        public Task<EmployeeDetailDto?> GetAsync(string ownerSubject, string employeeId, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(employee.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase) ? employee : null);

        public Task<EmployeeDetailDto?> FindAsync(string employeeId, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(employee.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase) ? employee : null);

        public Task<bool> ExistsNameAsync(string ownerSubject, string displayName, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<EmployeeDetailDto> UpsertAsync(string ownerSubject, EmployeeDetailDto employee, CancellationToken cancellationToken = default)
            => Task.FromResult(employee);

        public Task<int> UpsertManyAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default)
            => Task.FromResult(employees.Count);

        public Task<int> ReplaceOwnerAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default)
            => Task.FromResult(employees.Count);
    }

    private sealed class RecordingEmployeeHiringService : IEmployeeHiringService
    {
        public string? LastGetHiringStatusHireId { get; private set; }
        public string? LastCreateWorkspaceTargetHireId { get; private set; }
        public string? LastUploadSkillHireId { get; private set; }

        public ApiResponse<HireTemplateResultDto> HireResponse { get; init; } = ApiResponse<HireTemplateResultDto>.ErrorResponse(500, "not configured");
        public ApiResponse<HiringStatusDto> StatusResponse { get; init; } = ApiResponse<HiringStatusDto>.ErrorResponse(500, "not configured");
        public ApiResponse<HireTemplateResultDto> CreateWorkspaceResponse { get; init; } = ApiResponse<HireTemplateResultDto>.ErrorResponse(500, "not configured");
        public ApiResponse<bool> UploadSkillResponse { get; init; } = ApiResponse<bool>.ErrorResponse(500, "not configured");

        public Task<ApiResponse<HireTemplateResultDto>> HireAsync(string templateId, HireTemplateRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(HireResponse);

        public Task<ApiResponse<HireTemplateResultDto>> CreateEvaluationWorkspaceAsync(string targetHireId, CancellationToken cancellationToken = default)
        {
            LastCreateWorkspaceTargetHireId = targetHireId;
            return Task.FromResult(CreateWorkspaceResponse);
        }

        public Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default)
        {
            LastGetHiringStatusHireId = hireId;
            return Task.FromResult(StatusResponse);
        }

        public Task<ApiResponse<StartHiringConversationResultDto>> StartConversationAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<StartHiringConversationResultDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringConversationControlResultDto>> PauseConversationAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringConversationControlResultDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringConversationControlResultDto>> ResumeConversationAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringConversationControlResultDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringConversationResultDto>> SendConversationMessageAsync(string hireId, HiringConversationMessageRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringConversationResultDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringConversationTimelineDto>> GetConversationTimelineAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringConversationTimelineDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(string hireId, string? stage, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringStagePreviewDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(string hireId, HiringAuditDecisionRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<IReadOnlyList<HiringAuditLogDto>>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringFinalizeResultDto>> FinalizeAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringFinalizeResultDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<HiringWorkflowStateDto>> GetWorkflowStateAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<HiringWorkflowStateDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<bool>> UploadEvaluationSkillAsync(string hireId, string? skillRootPath = null, CancellationToken cancellationToken = default)
        {
            LastUploadSkillHireId = hireId;
            return Task.FromResult(UploadSkillResponse);
        }

        public Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
            => throw new NotSupportedException();

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

    private sealed class NoopEvaluationAssetStore : IEvaluationAssetStore
    {
        public Task<StoredEvaluationAsset> SaveTextAsync(string sessionId, int iteration, string assetType, string fileName, string content, string mimeType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StoredEvaluationAsset> SaveBytesAsync(string sessionId, int iteration, string assetType, string fileName, byte[] content, string mimeType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubRequestContextService(string ownerSubject) : IRequestContextService
    {
        public string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null) => ownerSubject;

        public (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
            => ("tenant-test", "operator-test");
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "HireBot.Core.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
