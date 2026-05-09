using System.IO.Compression;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Providers;
using HireBot.Core.Services.EmployeeRuntime;
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
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;

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
                    [new HiringConversationMessageDto("msg-001", "assistant", "Hello", DateTimeOffset.UtcNow)],
                    [])),
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-001",
                    "session-001",
                    "goal",
                    false,
                    new HiringConversationMessageDto("msg-002", "assistant", "Received", DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        "hire-001",
                        "goal",
                        "discovery",
                        "summary",
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow))),
            SessionDetailResponse = ApiResponse<SandboxSessionDetailDto>.SuccessResponse(
                new SandboxSessionDetailDto(
                    "session-001",
                    [],
                    [],
                    true))
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
                Content = "Continue the workflow."
            });

        Assert.True(sendResult.Success);
        Assert.Equal("Continue the workflow.", sandboxService.LastSendMessageRequest!.Content);
        Assert.Equal("hire-001", sandboxService.LastSendMessageRequest.ScopeKey);
        Assert.Equal("hire-001", sandboxService.LastSessionDetailRequest!.ScopeKey);

        var timelineResult = await service.GetConversationTimelineAsync("hire-001");
        Assert.True(timelineResult.Success);
        Assert.Null(sandboxService.LastTimelineRequest);
        Assert.Contains(timelineResult.Data!.Messages, message => message.Role == "user");
        Assert.Contains(timelineResult.Data.Messages, message => message.Role == "assistant");
    }

    [Fact]
    public async Task EmployeeHiringService_SendConversationMessageAsync_ShouldAdvanceStageFromSessionHandoffMetadata()
    {
        var sandboxService = new RecordingSandboxService
        {
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-001",
                    "session-001",
                    HiringCollectionStage.Material,
                    false,
                    new HiringConversationMessageDto(
                        "msg-handoff-ready",
                        "assistant",
                        "Please continue.",
                        DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        "hire-001",
                        HiringCollectionStage.Material,
                        "employment-coach-conversation",
                        "Please continue.",
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow))),
            SessionDetailResponse = ApiResponse<SandboxSessionDetailDto>.SuccessResponse(
                new SandboxSessionDetailDto(
                    "session-001",
                    [],
                    BuildReadyForPackagingHandoffs("refund-flow.pdf", "Extract refund flow nodes and rules"),
                    true))
        };
        var service = CreateEmployeeHiringService(
            sandboxService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new HttpContextAccessor
            {
                HttpContext = CreateHttpContext("tenant-1", "operator-1")
            });

        var sendResult = await service.SendConversationMessageAsync(
            "hire-001",
            new HiringConversationMessageRequestDto
            {
                Content = "Here is the refund flow material.",
                Materials =
                [
                    new HiringConversationMaterialDto
                    {
                        Type = "file",
                        Name = "refund-flow.pdf",
                        Content = "refund flow"
                    }
                ]
            });

        Assert.True(sendResult.Success);
        Assert.Equal(HiringCollectionStage.ReadyForPackaging, sendResult.Data!.CurrentStage);

        var workflowStateResult = await service.GetWorkflowStateAsync("hire-001");
        Assert.True(workflowStateResult.Success);
        Assert.Equal(HiringCollectionStage.ReadyForPackaging, workflowStateResult.Data!.CurrentStage);
        Assert.Equal(HiringCollectionPhase.ReadyForFinalize, workflowStateResult.Data.CollectionPhase);
        Assert.True(workflowStateResult.Data.LatestDiagnosticReport!.ReadyForPackaging);

        var materialReadiness = Assert.Single(
            workflowStateResult.Data.StageReadiness!,
            item => string.Equals(item.Stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HiringStageReadinessStatus.Complete, materialReadiness.Status);
        Assert.Empty(materialReadiness.BlockingHandoffIds);

        var skillReadiness = Assert.Single(
            workflowStateResult.Data.StageReadiness!,
            item => string.Equals(item.Stage, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HiringStageReadinessStatus.Complete, skillReadiness.Status);

        var externalReadiness = Assert.Single(
            workflowStateResult.Data.StageReadiness!,
            item => string.Equals(item.Stage, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HiringStageReadinessStatus.Complete, externalReadiness.Status);
        Assert.Equal(3, workflowStateResult.Data.HandoffItems!.Count);
    }

    [Fact]
    public async Task EmployeeHiringService_SendConversationMessageAsync_ShouldAcceptCanonicalHandoffMetadata()
    {
        var sandboxService = new RecordingSandboxService
        {
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-001",
                    "session-001",
                    HiringCollectionStage.Material,
                    false,
                    new HiringConversationMessageDto(
                        "msg-canonical-handoff",
                        "assistant",
                        "Please continue.",
                        DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        "hire-001",
                        HiringCollectionStage.Material,
                        "employment-coach-conversation",
                        "Please continue.",
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow))),
            SessionDetailResponse = ApiResponse<SandboxSessionDetailDto>.SuccessResponse(
                new SandboxSessionDetailDto(
                    "session-001",
                    [],
                    BuildReadyForPackagingHandoffs("refund-process.pdf", "Extract refund process branches"),
                    true))
        };

        var service = CreateEmployeeHiringService(
            sandboxService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new HttpContextAccessor
            {
                HttpContext = CreateHttpContext("tenant-1", "operator-1")
            });

        var sendResult = await service.SendConversationMessageAsync(
            "hire-001",
            new HiringConversationMessageRequestDto
            {
                Content = "Here is the refund process material.",
                Materials =
                [
                    new HiringConversationMaterialDto
                    {
                        Type = "file",
                        Name = "refund-process.pdf",
                        Content = "refund process"
                    }
                ]
            });

        Assert.True(sendResult.Success);

        var workflowStateResult = await service.GetWorkflowStateAsync("hire-001");
        Assert.True(workflowStateResult.Success);
        Assert.Equal(HiringCollectionStage.ReadyForPackaging, workflowStateResult.Data!.CurrentStage);
        Assert.Equal(HiringCollectionPhase.ReadyForFinalize, workflowStateResult.Data.CollectionPhase);

        var materialReadiness = Assert.Single(
            workflowStateResult.Data.StageReadiness!,
            item => string.Equals(item.Stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HiringStageReadinessStatus.Complete, materialReadiness.Status);
        Assert.Empty(materialReadiness.BlockingHandoffIds);
        Assert.Equal(3, workflowStateResult.Data.HandoffItems!.Count);
    }

    [Fact]
    public async Task EmployeeHiringService_SendConversationMessageAsync_ShouldShortcutToPackagingAfterConfirmedHandoffs()
    {
        var sandboxService = new RecordingSandboxService
        {
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-001",
                    "session-001",
                    HiringCollectionStage.Material,
                    false,
                    new HiringConversationMessageDto(
                        "msg-demo-shortcut",
                        "assistant",
                        "Received the material.",
                        DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        "hire-001",
                        HiringCollectionStage.Material,
                        "employment-coach-conversation",
                        "Received the material.",
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow))),
            SessionDetailResponse = ApiResponse<SandboxSessionDetailDto>.SuccessResponse(
                new SandboxSessionDetailDto(
                    "session-001",
                    [],
                    BuildReadyForPackagingHandoffs("demo-playbook.pdf", "Extract demo workflow nodes"),
                    true))
        };

        var service = CreateEmployeeHiringService(
            sandboxService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new HttpContextAccessor
            {
                HttpContext = CreateHttpContext("tenant-1", "operator-1")
            });

        var sendResult = await service.SendConversationMessageAsync(
            "hire-001",
            new HiringConversationMessageRequestDto
            {
                Content = "This is the demo material.",
                Materials =
                [
                    new HiringConversationMaterialDto
                    {
                        Type = "file",
                        Name = "demo-playbook.pdf",
                        Content = "demo playbook"
                    }
                ]
            });

        Assert.True(sendResult.Success);
        Assert.Equal(HiringCollectionStage.ReadyForPackaging, sendResult.Data!.CurrentStage);

        var workflowStateResult = await service.GetWorkflowStateAsync("hire-001");
        Assert.True(workflowStateResult.Success);
        Assert.Equal(HiringCollectionStage.ReadyForPackaging, workflowStateResult.Data!.CurrentStage);
        Assert.Equal(HiringCollectionPhase.ReadyForFinalize, workflowStateResult.Data.CollectionPhase);
        Assert.True(workflowStateResult.Data.LatestDiagnosticReport!.ReadyForPackaging);
        Assert.All(
            workflowStateResult.Data.StageReadiness!,
            item => Assert.Equal(HiringStageReadinessStatus.Complete, item.Status));
        Assert.Equal(3, workflowStateResult.Data.HandoffItems!.Count);
    }

    [Fact]
    public async Task EmployeeHiringService_SendConversationMessageAsync_ShouldFailWhenHandoffMetadataInvalid()
    {
        var sandboxService = new RecordingSandboxService
        {
            SendMessageResponse = ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    "hire-001",
                    "session-001",
                    HiringCollectionStage.Material,
                    false,
                    new HiringConversationMessageDto(
                        "msg-invalid-handoff",
                        "assistant",
                        "Please continue.",
                        DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        "hire-001",
                        HiringCollectionStage.Material,
                        "employment-coach-conversation",
                        "Please continue.",
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow))),
            SessionDetailResponse = ApiResponse<SandboxSessionDetailDto>.SuccessResponse(
                new SandboxSessionDetailDto(
                    "session-001",
                    [],
                    [
                        BuildHandoffItem(
                            handoffId: "handoff-material-invalid",
                            title: "Material classification",
                            stage: HiringCollectionStage.Material,
                            targetSkill: "ontology-extraction",
                            status: "done",
                            payloadJson: "{\"objective\":\"Extract material summary\",\"source_files\":[\"refund-flow.pdf\"]}")
                    ],
                    true))
        };
        var service = CreateEmployeeHiringService(sandboxService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendConversationMessageAsync(
            "hire-001",
            new HiringConversationMessageRequestDto
            {
                Content = "Here is the refund material."
            }));

        Assert.Contains("status", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmployeeHiringService_StartConversationAsync_ShouldRecoverOwnerFromSandboxRegistryAfterRestart()
    {
        var sandboxService = new RecordingSandboxService
        {
            EnsureSessionResponse = ApiResponse<StartHiringConversationResultDto>.SuccessResponse(
                new StartHiringConversationResultDto("hire-001", "session-001", HiringCollectionStage.Material, false, []))
        };

        using var dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        dbContext.SandboxInstances.Add(new SandboxInstanceEntity
        {
            SandboxId = "sandbox-001",
            ScopeType = SandboxScopeTypes.Hire,
            ScopeKey = "hire-001",
            SandboxRole = "hiring",
            ProvisioningMode = "external",
            OwnerSubject = "owner-from-db",
            TenantId = "tenant-from-db",
            OperatorId = "operator-from-db",
            State = "Running",
            GatewayEndpoint = "http://sandbox-gateway.local/"
        });
        dbContext.SaveChanges();

        var service = CreateEmployeeHiringService(
            sandboxService,
            dbContext,
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext()
            },
            seedRuntimeContext: false);

        var timelineResult = await service.StartConversationAsync("hire-001");

        Assert.True(timelineResult.Success);
        Assert.Equal("owner-from-db", sandboxService.LastEnsureSessionRequest!.OwnerSubject);
        Assert.Equal("tenant-from-db", sandboxService.LastEnsureSessionRequest.TenantId);
        Assert.Equal("operator-from-db", sandboxService.LastEnsureSessionRequest.OperatorId);
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
                    new HiringConversationMessageDto("msg-010", "assistant", "Evaluation ready.", DateTimeOffset.UtcNow),
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
                Content = "Please evaluate the draft."
            },
            CancellationToken.None);

        Assert.True(sendResult.Success);
        Assert.Equal("Please evaluate the draft.", sandboxService.LastSendMessageRequest!.Content);
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
            NullLogger<EvaluationService>.Instance,
            new NoopSystemSkillRegistry(),
            new KingCrabSandboxTokenProvider(
                new NoopHttpClientFactory(),
                new ConfigurationBuilder().Build(),
                NullLogger<KingCrabSandboxTokenProvider>.Instance));

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
        => CreateEmployeeHiringService(
            sandboxService,
            CreateDbContext(Guid.NewGuid().ToString("N")),
            new HttpContextAccessor
            {
                HttpContext = CreateHttpContext("tenant-1", "operator-1")
            });

    private static EmployeeHiringService CreateEmployeeHiringService(
        RecordingSandboxService sandboxService,
        HireBotDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<EmployeeHiringService>? logger = null,
        bool seedRuntimeContext = true)
    {
        var runtimeStore = new PersistentHiringRuntimeStore(dbContext);
        if (seedRuntimeContext)
        {
            runtimeStore.Upsert(BuildRuntimeContext("hire-001", "sandbox-001", "tenant-1:operator-1", "tenant-1", "operator-1"));
        }

        return new EmployeeHiringService(
            new NoopTemplateDataProvider(),
            new NoopTemplatePackageProvider(),
            new NoopDiscoveryRoleTemplatePackageProvider(),
            new NoopWorkingTemplatePackageProvider(),
            new NoopDiscoveryRuleProvider(),
            new NoopSystemSkillRegistry(),
            new HiringStageCompletionEvaluator(),
            runtimeStore,
            new ThrowingKingCrabHttpClient(),
            sandboxService,
            new ConfigurationBuilder().Build(),
            CreateDataProtectionProvider(),
            httpContextAccessor,
            new SimpleServiceScopeFactory(),
            dbContext,
            new NoopHiringFileStore(),
            new NoopInstanceArtifactCloneService(),
            new NoopHiringArtifactPackageService(),
            logger ?? NullLogger<EmployeeHiringService>.Instance,
            new StubHostEnvironment());
    }

    private static IDataProtectionProvider CreateDataProtectionProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private static HiringRuntimeContext BuildRuntimeContext(
        string hireId,
        string sandboxId,
        string ownerSubject,
        string tenantId,
        string operatorId)
    {
        return new HiringRuntimeContext
        {
            HireId = hireId,
            TemplateId = "template-001",
            TemplateName = "Template 001",
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            SandboxId = sandboxId,
            SessionId = "session-seeded",
            CurrentStage = HiringCollectionStage.Material,
            CollectionPhase = HiringCollectionPhase.InProgress,
            IsConversationPaused = false,
            IsConversationResponding = false,
            ReferenceTemplatePackage = new TemplatePackageDefinition(
                RequestedTemplateId: "template-001",
                PackageId: "template-001",
                PackageVersion: "1.0.0",
                PackageHash: "hash-template-001",
                SourceArchive: null,
                PackageRootPath: "template-001",
                ManifestJson: "{}",
                DisplayName: "Template 001",
                Description: "test",
                PackageFiles: [],
                OntologySlices: [],
                RequiredSkills: [],
                EntrySkill: null,
                StageRules: []),
            RoleTemplatePackage = new TemplatePackageDefinition(
                RequestedTemplateId: "template-001",
                PackageId: "template-001",
                PackageVersion: "1.0.0",
                PackageHash: "hash-template-001",
                SourceArchive: null,
                PackageRootPath: "template-001",
                ManifestJson: "{}",
                DisplayName: "Template 001",
                Description: "test",
                PackageFiles: [],
                OntologySlices: [],
                RequiredSkills: [],
                EntrySkill: null,
                StageRules: []),
            WorkingTemplatePackage = new TemplatePackageDefinition(
                RequestedTemplateId: "template-001",
                PackageId: "template-001",
                PackageVersion: "1.0.0",
                PackageHash: "hash-template-001",
                SourceArchive: null,
                PackageRootPath: "template-001",
                ManifestJson: "{}",
                DisplayName: "Template 001",
                Description: "test",
                PackageFiles: [],
                OntologySlices: [],
                RequiredSkills: [],
                EntrySkill: null,
                StageRules: []),
            DiscoverySkill = new DiscoverySkillDefinition(
                SkillId: "employment-coach-conversation",
                SkillVersion: "1.0.0",
                SkillHash: "hash-discovery",
                SkillRootPath: "employment-coach-conversation",
                SkillContent: "# discovery",
                Files:
                [
                    new DiscoverySkillFileAsset("SKILL.md", "# discovery", "hash-skill")
                ],
                StageRules:
                [
                    new DiscoveryStageRule(HiringCollectionStage.Material, "employment-coach-conversation", "material", ["business_goal"]),
                    new DiscoveryStageRule(HiringCollectionStage.Skill, "skill_generation", "skill", ["skill_blueprint"]),
                    new DiscoveryStageRule(HiringCollectionStage.External, "external_config", "external", ["external_dependencies"]),
                    new DiscoveryStageRule(HiringCollectionStage.ReadyForPackaging, "diagnosis", "package", [])
                ]),
            StructuredData = new Dictionary<string, string?>(),
            Materials = [],
            Messages = [],
            AuditLogs = [],
            StageCompletion = []
        };
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
            NullLogger<EvaluationService>.Instance,
            new NoopSystemSkillRegistry(),
            new KingCrabSandboxTokenProvider(
                new NoopHttpClientFactory(),
                new ConfigurationBuilder().Build(),
                NullLogger<KingCrabSandboxTokenProvider>.Instance));
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

    private static IReadOnlyList<SandboxSessionHandoffItemDto> BuildReadyForPackagingHandoffs(string materialFileName, string materialObjective)
    {
        return
        [
            BuildHandoffItem(
                handoffId: "handoff-material-001",
                title: "Material classification",
                stage: HiringCollectionStage.Material,
                targetSkill: "ontology-extraction",
                status: HiringHandoffStatus.Confirmed,
                intent: "Summarize refund flow material",
                category: "process_sop",
                source: "uploaded material",
                acceptance: "objective and source_files are present",
                payloadJson: $$"""
                {
                  "objective": "{{materialObjective}}",
                  "source_files": ["{{materialFileName}}"]
                }
                """,
                relatedFiles: [materialFileName]),
            BuildHandoffItem(
                handoffId: "handoff-skill-001",
                title: "Skill baseline confirmation",
                stage: HiringCollectionStage.Skill,
                targetSkill: "skill-generation",
                status: HiringHandoffStatus.Confirmed,
                intent: "Confirm baseline skills",
                category: "skill_baseline",
                source: "coach confirmation",
                acceptance: "payload.skills covers the baseline",
                payloadJson: "{\"skills\":[{\"skill_name\":\"refund-workflow\",\"generation_action\":\"reuse\",\"origin\":\"baseline\"}]}") ,
            BuildHandoffItem(
                handoffId: "handoff-external-001",
                title: "External capability confirmation",
                stage: HiringCollectionStage.External,
                targetSkill: "external-config",
                status: HiringHandoffStatus.Confirmed,
                intent: "Confirm external capability scope",
                category: "external_capability",
                source: "coach confirmation",
                acceptance: "external capability is fully defined",
                payloadJson: "{\"external_capabilities\":[{\"category\":\"crm\",\"objective\":\"read refund orders\",\"target_system\":\"crm-demo\",\"auth_kind\":\"none\",\"linked_skills\":[\"refund-workflow\"]}]}")
        ];
    }

    private static SandboxSessionHandoffItemDto BuildHandoffItem(
        string handoffId,
        string title,
        string stage,
        string targetSkill,
        string status,
        string? payloadJson = null,
        string? intent = null,
        string? category = null,
        string? source = "test-source",
        string? acceptance = "artifact exists",
        string? fingerprint = null,
        IReadOnlyList<string>? relatedHandoffIds = null,
        IReadOnlyList<string>? relatedFiles = null,
        string sessionId = "session-001",
        string workflowId = "employment-coach",
        int revision = 1,
        string createdAtUtc = "2026-05-06T10:00:00Z",
        string updatedAtUtc = "2026-05-06T10:05:00Z")
    {
        var payload = string.IsNullOrWhiteSpace(payloadJson)
            ? JsonSerializer.SerializeToElement(new Dictionary<string, object?>())
            : JsonDocument.Parse(payloadJson).RootElement.Clone();

        return new SandboxSessionHandoffItemDto(
            SessionId: sessionId,
            WorkflowId: workflowId,
            HandoffId: handoffId,
            Title: title,
            Kind: HiringHandoffKind.HandoffTodo,
            Stage: stage,
            TargetSkill: targetSkill,
            Intent: intent,
            Category: category,
            Payload: payload,
            Source: source,
            Acceptance: acceptance,
            Status: status,
            Fingerprint: fingerprint ?? BuildTestHandoffFingerprint(stage, targetSkill, handoffId),
            RelatedHandoffIds: relatedHandoffIds ?? [],
            RelatedFiles: relatedFiles ?? [],
            Revision: revision,
            CreatedAtUtc: DateTimeOffset.Parse(createdAtUtc),
            UpdatedAtUtc: DateTimeOffset.Parse(updatedAtUtc),
            DispatchId: null,
            CallbackSummary: null);
    }

    private static string BuildTestHandoffFingerprint(string stage, string targetSkill, string handoffId)
    {
        var normalizedTarget = targetSkill.Trim().Replace('-', '_').ToLowerInvariant();
        return $"{stage}:{normalizedTarget}:{handoffId}";
    }

    private sealed class RecordingSandboxService : ISandboxService
    {
        public SandboxEnsureSessionRequestDto? LastEnsureSessionRequest { get; private set; }

        public SandboxSendMessageRequestDto? LastSendMessageRequest { get; private set; }

        public SandboxTimelineRequestDto? LastTimelineRequest { get; private set; }

        public SandboxSessionDetailRequestDto? LastSessionDetailRequest { get; private set; }

        public ApiResponse<StartHiringConversationResultDto> EnsureSessionResponse { get; init; } =
            ApiResponse<StartHiringConversationResultDto>.ErrorResponse(500, "not configured");

        public ApiResponse<HiringConversationResultDto> SendMessageResponse { get; init; } =
            ApiResponse<HiringConversationResultDto>.ErrorResponse(500, "not configured");

        public ApiResponse<HiringConversationTimelineDto> TimelineResponse { get; init; } =
            ApiResponse<HiringConversationTimelineDto>.ErrorResponse(500, "not configured");

        public ApiResponse<SandboxSessionDetailDto> SessionDetailResponse { get; init; } =
            ApiResponse<SandboxSessionDetailDto>.ErrorResponse(500, "not configured");

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

        public Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(SandboxSessionDetailRequestDto request, CancellationToken cancellationToken = default)
        {
            LastSessionDetailRequest = request;
            return Task.FromResult(SessionDetailResponse);
        }

        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SandboxAttachmentUploadResultDto>.ErrorResponse(501, "not used"));

        public Task<ApiResponse<SkillPackageUploadResultDto>> UploadSkillPackageAsync(SkillPackageUploadRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<SkillPackageUploadResultDto>.ErrorResponse(501, "not used"));

        public Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken = default)
            => Task.FromResult<SandboxInstanceDto?>(null);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NoopScope : IDisposable
        {
            public static NoopScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
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

    private sealed class NoopDiscoveryRoleTemplatePackageProvider : IDiscoveryRoleTemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopWorkingTemplatePackageProvider : IWorkingTemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default)
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

        public Task<ApiResponse<HiringConversationResultDto>> SyncConversationTurnAsync(string hireId, HiringConversationSyncRequestDto request, CancellationToken cancellationToken = default)
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

        public Task<ApiResponse<HiringWorkflowStateDto>> UpsertCredentialBindingAsync(string hireId, HiringCredentialBindingRequestDto request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<HiringWorkflowStateDto>> UpdateConfigFileAsync(string hireId, string configKey, HiringConfigFileUpdateRequestDto request, CancellationToken cancellationToken = default)
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
            => Task.FromResult(new HiringArtifactPackageSnapshotDto(
                request.HireId,
                request.SessionId,
                HiringArtifactPackageKinds.IntermediatePackageZip,
                request.FileName,
                $"packages/intermediate/{request.FileName}",
                "sha256",
                [],
                false));

        public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(new HiringArtifactPackageSnapshotDto(
                request.HireId,
                request.SessionId,
                HiringArtifactPackageKinds.FinalPackageZip,
                request.FileName,
                $"packages/final/{request.FileName}",
                "sha256",
                [],
                true));

        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(string hireId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
