using System.Reflection;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Hiring.Artifacts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.StoreSkills;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;

namespace HireBot.Core.Tests;

public class PackagingTestCasesFromHistoryTests
{
    private static readonly JsonSerializerOptions CallbackJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private const string ValidTestCasesPayload = """
        {
          "description": "雇佣评估",
          "role": "digital_employee",
          "industry": "general",
          "test_cases": [
            {
              "test_case_id": "TC-001",
              "scenario_name": "咨询业务",
              "input": { "user_request": "请介绍业务流程", "context": {} },
              "expected_behavior_sequence": [
                { "step": 1, "action": "理解需求", "criteria": "准确" },
                { "step": 2, "action": "给出方案", "criteria": "完整" }
              ],
              "expected_output": {
                "resolution": "已解答",
                "user_satisfaction": "满意",
                "artifacts_created": []
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task InvokePackagingTestCasesSkillAsync_WhenSkillReturnsCallback_ShouldReturnMergedBundle()
    {
        var context = CreateRuntimeContext();
        var sandbox = CreateSandboxFake(skillReply: BuildSkillSuccessReply(includeExtendedBundle: true));
        var service = EmployeeHiringServicePackagingTestFactory.Create(sandbox, context);

        var (success, bundle) = await service.InvokePackagingTestCasesSkillAsync(context, CancellationToken.None);

        Assert.True(success);
        Assert.NotNull(bundle);
        Assert.Contains("packaging-merged", bundle.MergedJson);
        Assert.Contains("TC-001", bundle.MergedJson);
        Assert.Contains("history-derived.json", bundle.SourcesIndexJson);
        Assert.Equal(1, sandbox.GetSessionDetailCallCount);
        Assert.Equal(1, sandbox.SendMessageCallCount);
    }

    [Fact]
    public async Task InvokePackagingTestCasesSkillAsync_WhenLegacyCallbackOnly_ShouldStillReturnMergedJson()
    {
        var context = CreateRuntimeContext();
        var sandbox = CreateSandboxFake(skillReply: BuildSkillSuccessReply(includeExtendedBundle: false));
        var service = EmployeeHiringServicePackagingTestFactory.Create(sandbox, context);

        var (success, bundle) = await service.InvokePackagingTestCasesSkillAsync(context, CancellationToken.None);

        Assert.True(success);
        Assert.NotNull(bundle);
        Assert.Contains("kingcrab-history-llm", bundle.MergedJson);
        Assert.True(string.IsNullOrWhiteSpace(bundle.SourcesIndexJson));
    }

    [Fact]
    public async Task InvokePackagingTestCasesSkillAsync_WhenSkillReturnsNoCallback_ShouldReturnFalse()
    {
        var context = CreateRuntimeContext();
        var sandbox = CreateSandboxFake(skillReply: "处理完成。");
        var service = EmployeeHiringServicePackagingTestFactory.Create(sandbox, context);

        var (success, _) = await service.InvokePackagingTestCasesSkillAsync(context, CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task InvokePackagingTestCasesSkillAsync_WhenSessionIdEmpty_ShouldReturnFalse()
    {
        var context = CreateRuntimeContext() with { SessionId = string.Empty };
        var service = EmployeeHiringServicePackagingTestFactory.Create(CreateSandboxFake(), context);

        var (success, _) = await service.InvokePackagingTestCasesSkillAsync(context, CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task InvokePackagingTestCasesSkillAsync_WhenAllInputsEmpty_ShouldReturnFalse()
    {
        var context = CreateRuntimeContext();
        var sandbox = CreateSandboxFake(messages: []);
        var service = EmployeeHiringServicePackagingTestFactory.Create(sandbox, context);

        var (success, _) = await service.InvokePackagingTestCasesSkillAsync(context, CancellationToken.None);

        Assert.False(success);
    }

    [Fact]
    public async Task InvokePackagingTestCasesSkillAsync_WhenHistoryEmptyButMaterialsPresent_ShouldStillInvoke()
    {
        var dbContext = EmployeeHiringServicePackagingTestFactory.CreateDbContext();
        var contentRoot = Directory.GetCurrentDirectory();
        var todoFilesRoot = HireBotPathResolver.ResolveTodoFilesRoot(contentRoot, null, null);
        var sessionDir = Path.Combine(todoFilesRoot, "session-001");
        Directory.CreateDirectory(sessionDir);
        var storagePath = Path.Combine(sessionDir, "rules.md");
        await File.WriteAllTextAsync(storagePath, "# 访客预约规则", Encoding.UTF8);

        dbContext.HiringMaterialFiles.Add(new HireBot.Repository.Entities.HiringMaterialFileEntity
        {
            HireId = "hire-packaging-history",
            SessionId = "session-001",
            RelativePath = "rules.md",
            OriginalFileName = "rules.md",
            StoragePath = storagePath,
            Format = "md",
            Sha256 = "abc",
            RequestedCategoryTitle = "访客预约与审核规则",
            TenantId = "tenant",
            OperatorId = "operator",
            UploadedBy = "user"
        });
        await dbContext.SaveChangesAsync();

        var context = CreateRuntimeContext();
        var sandbox = CreateSandboxFake(messages: [], skillReply: BuildSkillSuccessReply(includeExtendedBundle: true));
        var service = EmployeeHiringServicePackagingTestFactory.Create(sandbox, context, dbContext);

        var (success, bundle) = await service.InvokePackagingTestCasesSkillAsync(context, CancellationToken.None);

        Assert.True(success);
        Assert.NotNull(bundle);
        Assert.Equal(1, sandbox.SendMessageCallCount);
    }

    [Fact]
    public async Task EnsurePackagingTestCasesStagedAsync_WhenSkillSucceeds_ShouldUploadBundleAndMarkStaged()
    {
        var context = CreateRuntimeContext(withTemplateFiles: true);
        var sandbox = CreateSandboxFake(skillReply: BuildSkillSuccessReply(includeExtendedBundle: true));
        var service = EmployeeHiringServicePackagingTestFactory.Create(sandbox, context);

        var updated = await InvokeEnsurePackagingTestCasesStagedAsync(service, context, CancellationToken.None);

        Assert.True(updated.PackagingTestCasesStaged);
        Assert.True(sandbox.UploadedJsonByFileName.TryGetValue("evaluation-test-cases.json", out var mergedUploadJson));
        Assert.Contains("packaging-merged", mergedUploadJson);
        Assert.True(sandbox.UploadedFileNames.Count >= 5);

        var files = updated.WorkingTemplatePackage.PackageFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.True(files.ContainsKey("testcases/evaluation-test-cases.json"));
        Assert.True(files.ContainsKey("ontology/hiring-session/testcases-sources-index.json"));
        Assert.True(files.ContainsKey("ontology/hiring-session/testcases-sources/materials-derived.json"));
        var packageJson = Encoding.UTF8.GetString(files["testcases/evaluation-test-cases.json"].Content);
        Assert.Contains("TC-001", packageJson);
    }

    [Fact]
    public async Task EnsurePackagingTestCasesStagedAsync_WhenSkillFails_ShouldFallbackAndStillStage()
    {
        var context = CreateRuntimeContext();
        var sandbox = CreateSandboxFake(skillReply: "无回调");
        var service = EmployeeHiringServicePackagingTestFactory.Create(sandbox, context);

        var updated = await InvokeEnsurePackagingTestCasesStagedAsync(service, context, CancellationToken.None);

        Assert.True(updated.PackagingTestCasesStaged);
        Assert.NotNull(sandbox.LastUploadedJson);
        Assert.Contains("packaging-fallback", sandbox.LastUploadedJson);
    }

    internal static string BuildSkillSuccessReply(bool includeExtendedBundle = true, string source = "packaging-merged")
    {
        var enriched = PackagingTestCasesJsonValidator.AppendPackagingMetadata(ValidTestCasesPayload.Trim(), source);
        object technicalArtifact;
        if (includeExtendedBundle)
        {
            var historyDerived = """{"description":"h","role":"digital_employee","industry":"general","source":"history-derived","test_cases":[]}""";
            var materialsDerived = """
                {
                  "description": "m",
                  "role": "digital_employee",
                  "industry": "general",
                  "source": "materials-derived",
                  "test_cases": [
                    {
                      "test_case_id": "TC-M01",
                      "scenario_name": "资料场景",
                      "input": { "user_request": "按规则审核", "context": {} },
                      "expected_behavior_sequence": [
                        { "step": 1, "action": "读规则", "criteria": "准确" },
                        { "step": 2, "action": "执行", "criteria": "合规" }
                      ],
                      "expected_output": { "resolution": "完成", "user_satisfaction": "满意", "artifacts_created": [] }
                    }
                  ]
                }
                """;
            var templateDerived = """
                {
                  "description": "t",
                  "role": "digital_employee",
                  "industry": "general",
                  "source": "template-derived",
                  "test_cases": [
                    {
                      "test_case_id": "TC-T01",
                      "scenario_name": "模板场景",
                      "input": { "user_request": "提交预约", "context": {} },
                      "expected_behavior_sequence": [
                        { "step": 1, "action": "受理", "criteria": "完整" },
                        { "step": 2, "action": "通知", "criteria": "及时" }
                      ],
                      "expected_output": { "resolution": "完成", "user_satisfaction": "满意", "artifacts_created": [] }
                    }
                  ]
                }
                """;
            var indexJson = """
                {
                  "generated_at": "2026-05-28T12:00:00Z",
                  "primary": "testcases/evaluation-test-cases.json",
                  "sources": {
                    "history": "ontology/hiring-session/testcases-sources/history-derived.json",
                    "materials": "ontology/hiring-session/testcases-sources/materials-derived.json",
                    "template": "ontology/hiring-session/testcases-sources/template-derived.json"
                  },
                  "inputs_summary": { "history_turns": 2, "material_files": 1, "template_files": 1 }
                }
                """;
            technicalArtifact = new
            {
                source,
                evaluation_test_cases_json = enriched,
                testcases_sources_index_json = indexJson,
                history_derived_json = historyDerived,
                materials_derived_json = materialsDerived,
                template_derived_json = templateDerived
            };
        }
        else
        {
            technicalArtifact = new
            {
                source = "kingcrab-history-llm",
                evaluation_test_cases_json = PackagingTestCasesJsonValidator.AppendPackagingMetadata(
                    ValidTestCasesPayload.Trim(),
                    "kingcrab-history-llm")
            };
        }

        var callback = new
        {
            source_dispatch_target = "packaging-test-cases",
            handoff_ids = Array.Empty<string>(),
            user_summary = "已生成测试用例",
            technical_artifact = technicalArtifact,
            artifacts = new[]
            {
                new
                {
                    path = "testcases/evaluation-test-cases.json",
                    kind = "evaluation_test_cases_json",
                    encoding = "utf8",
                    content = enriched,
                    sha256 = string.Empty
                }
            },
            todo_results = Array.Empty<object>(),
            status = "success",
            errors = Array.Empty<string>()
        };

        var json = JsonSerializer.Serialize(callback, CallbackJsonOptions);
        return $"<dispatch_callback>{json}</dispatch_callback>";
    }

    internal static PackagingSandboxFake CreateSandboxFake(
        IReadOnlyList<HiringConversationMessageDto>? messages = null,
        string? skillReply = null)
    {
        messages ??=
        [
            new HiringConversationMessageDto("1", "user", "请介绍业务流程", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("2", "assistant", "流程如下...", DateTimeOffset.UtcNow)
        ];

        return new PackagingSandboxFake(messages, skillReply ?? BuildSkillSuccessReply());
    }

    private static HiringRuntimeContext CreateRuntimeContext(bool withTemplateFiles = false)
    {
        var packageFiles = withTemplateFiles
            ?
            [
                new TemplatePackageFileAsset(
                    "manifest.json",
                    Encoding.UTF8.GetBytes("""{"name":"Visitor Experience Pilot"}"""),
                    "hash-manifest"),
                new TemplatePackageFileAsset(
                    "skills/visitor-orchestrator/SKILL.md",
                    Encoding.UTF8.GetBytes("# Visitor Skill"),
                    "hash-skill")
            ]
            : Array.Empty<TemplatePackageFileAsset>();

        var templatePackage = new TemplatePackageDefinition(
            RequestedTemplateId: "default",
            PackageId: "pkg",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            SourceArchive: null,
            PackageRootPath: "Assets/TemplatePackages/default/NCrewTemplate",
            ManifestJson: "{\"name\":\"pkg\"}",
            DisplayName: "pkg",
            Description: "desc",
            PackageFiles: packageFiles,
            OntologySlices: [],
            Skills: [],
            RequiredSkills: [],
            EntrySkill: null,
            StageRules: []);

        return new HiringRuntimeContext
        {
            HireId = "hire-packaging-history",
            TemplateId = "default",
            TemplateName = "template",
            OwnerSubject = "owner",
            TenantId = "tenant",
            OperatorId = "operator",
            SandboxId = "sandbox",
            SessionId = "session-001",
            CurrentStage = HiringCollectionStage.ReadyForPackaging,
            CollectionPhase = "in_progress",
            RoleTemplatePackage = templatePackage,
            WorkingTemplatePackage = templatePackage,
            DiscoverySkill = new DiscoverySkillDefinition(
                SkillId: "employment-coach-conversation",
                SkillVersion: "1.0.0",
                SkillHash: "hash",
                SkillRootPath: "Assets/DigitalEmployeeTemplates/employment-coach-conversation",
                SkillContent: "# discovery",
                Files: [],
                StageRules: [])
        };
    }

    private static async Task<HiringRuntimeContext> InvokeEnsurePackagingTestCasesStagedAsync(
        EmployeeHiringService service,
        HiringRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var method = typeof(EmployeeHiringService).GetMethod(
            "EnsurePackagingTestCasesStagedAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<HiringRuntimeContext>)method.Invoke(service, [context, cancellationToken])!;
        return await task;
    }

    internal sealed class PackagingSandboxFake(
        IReadOnlyList<HiringConversationMessageDto> messages,
        string skillReply) : ISandboxService
    {
        public int GetSessionDetailCallCount { get; private set; }
        public int SendMessageCallCount { get; private set; }
        public string? LastUploadedJson { get; private set; }
        public List<string> UploadedFileNames { get; } = [];
        public Dictionary<string, string> UploadedJsonByFileName { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(
            SandboxSessionDetailRequestDto request,
            CancellationToken cancellationToken = default)
        {
            GetSessionDetailCallCount++;
            return Task.FromResult(ApiResponse<SandboxSessionDetailDto>.SuccessResponse(
                new SandboxSessionDetailDto("session-001", messages, [], true)));
        }

        public Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(
            SandboxSendMessageRequestDto request,
            CancellationToken cancellationToken = default)
        {
            SendMessageCallCount++;
            return Task.FromResult(ApiResponse<HiringConversationResultDto>.SuccessResponse(
                new HiringConversationResultDto(
                    request.ScopeKey,
                    "session-001",
                    HiringCollectionStage.ReadyForPackaging,
                    false,
                    new HiringConversationMessageDto("assistant-skill", "assistant", skillReply, DateTimeOffset.UtcNow),
                    new HiringStagePreviewDto(
                        request.ScopeKey,
                        HiringCollectionStage.ReadyForPackaging,
                        "employment-coach-conversation",
                        string.Empty,
                        new Dictionary<string, string?>(),
                        [],
                        [],
                        false,
                        DateTimeOffset.UtcNow),
                    false,
                    false)));
        }

        public Task<ApiResponse<SandboxWorkspaceUploadResultDto>> UploadWorkspaceFileAsync(
            SandboxWorkspaceUploadRequestDto request,
            CancellationToken cancellationToken = default)
        {
            LastUploadedJson = Encoding.UTF8.GetString(request.Content);
            UploadedFileNames.Add(request.FileName);
            UploadedJsonByFileName[request.FileName] = LastUploadedJson;
            return Task.FromResult(ApiResponse<SandboxWorkspaceUploadResultDto>.SuccessResponse(
                new SandboxWorkspaceUploadResultDto([request.FileName], 1, $"/workspace/{request.TargetDir}")));
        }

        public Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(SandboxRegisterRequestDto request, CancellationToken cancellationToken = default) => Throw();
        public Task<ApiResponse<SandboxInstanceDto>> CreateAsync(SandboxCreateRequestDto request, CancellationToken cancellationToken = default) => Throw();
        public Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Throw();
        public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Throw();
        public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Throw();
        public Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => Throw();
        public Task<ApiResponse<bool>> DeleteAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => ThrowBool();
        public Task<ApiResponse<IReadOnlyList<SandboxInstanceDto>>> ListByOwnerAsync(string ownerSubject, CancellationToken cancellationToken = default) => ThrowList();
        public Task<ApiResponse<bool>> DeleteForOwnerAsync(string sandboxId, string ownerSubject, CancellationToken cancellationToken = default) => ThrowBool();
        public Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(SandboxEnsureSessionRequestDto request, CancellationToken cancellationToken = default) => ThrowEnsureSession();
        public Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(SandboxTimelineRequestDto request, CancellationToken cancellationToken = default) => ThrowTimeline();
        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default) => ThrowAttachment();
        public Task<ApiResponse<DigitalEmployeeTemplateUploadResultDto>> UploadDigitalEmployeeTemplateAsync(DigitalEmployeeTemplateUploadRequestDto request, CancellationToken cancellationToken = default) => ThrowTemplate();
        public Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken = default) => Task.FromResult<SandboxInstanceDto?>(null);

        private static Task<ApiResponse<SandboxInstanceDto>> Throw() => throw new NotSupportedException();
        private static Task<ApiResponse<bool>> ThrowBool() => throw new NotSupportedException();
        private static Task<ApiResponse<IReadOnlyList<SandboxInstanceDto>>> ThrowList() => throw new NotSupportedException();
        private static Task<ApiResponse<StartHiringConversationResultDto>> ThrowEnsureSession() => throw new NotSupportedException();
        private static Task<ApiResponse<HiringConversationTimelineDto>> ThrowTimeline() => throw new NotSupportedException();
        private static Task<ApiResponse<SandboxAttachmentUploadResultDto>> ThrowAttachment() => throw new NotSupportedException();
        private static Task<ApiResponse<DigitalEmployeeTemplateUploadResultDto>> ThrowTemplate() => throw new NotSupportedException();
    }
}

internal static class EmployeeHiringServicePackagingTestFactory
{
    public static EmployeeHiringService Create(
        ISandboxService sandboxService,
        HiringRuntimeContext? seedContext = null,
        HireBotDbContext? dbContext = null,
        IHiringArtifactPackageService? artifactPackageService = null,
        ITemplateDataProvider? templateDataProvider = null,
        IStoreSkillPackageDownloader? storeSkillPackageDownloader = null)
    {
        dbContext ??= CreateDbContext();
        var configuration = new ConfigurationBuilder().Build();
        var hiringRuntimeStore = new InMemoryHiringRuntimeStore();
        if (seedContext is not null)
        {
            hiringRuntimeStore.Upsert(seedContext);
        }

        return new EmployeeHiringService(
            templateDataProvider ?? new NotSupportedTemplateDataProvider(),
            new NotSupportedTemplatePackageProvider(),
            new NotSupportedDiscoveryRoleTemplatePackageProvider(),
            new NotSupportedWorkingTemplatePackageProvider(),
            new NotSupportedDiscoveryRuleProvider(),
            new HiringStageCompletionEvaluator(),
            hiringRuntimeStore,
            new NotSupportedKingCrabHttpClient(),
            sandboxService,
            new HttpContextAccessor(),
            new NotSupportedServiceScopeFactory(),
            dbContext,
            new NotSupportedHiringFileStore(),
            new NoOpInstanceArtifactCloneService(),
            artifactPackageService ?? new NotSupportedHiringArtifactPackageService(),
            storeSkillPackageDownloader ?? new NotSupportedStoreSkillPackageDownloader(),
            new PassThroughSecretProtector(),
            configuration,
            new TestHostEnvironment(),
            NullLogger<EmployeeHiringService>.Instance);
    }

    internal static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private sealed class NotSupportedTemplateDataProvider : ITemplateDataProvider
    {
        public Task<IReadOnlyList<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default) => Throw<IReadOnlyList<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition>>();
        public Task<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default) => Throw<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition?>();
    }

    internal sealed class StubTemplateDataProvider : ITemplateDataProvider
    {
        public Task<IReadOnlyList<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition>>([]);

        public Task<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default) =>
            Task.FromResult<HireBot.Abstraction.Models.EmployeeTemplate.EmployeeTemplateDefinition?>(null);
    }

    private sealed class NotSupportedTemplatePackageProvider : ITemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default) => Throw<TemplatePackageDefinition>();
    }

    private sealed class NotSupportedDiscoveryRoleTemplatePackageProvider : IDiscoveryRoleTemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default) => Throw<TemplatePackageDefinition>();
    }

    private sealed class NotSupportedWorkingTemplatePackageProvider : IWorkingTemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default) => Throw<TemplatePackageDefinition>();
    }

    private sealed class NotSupportedDiscoveryRuleProvider : IDiscoveryRuleProvider
    {
        public Task<DiscoverySkillDefinition> LoadAsync(CancellationToken cancellationToken = default) => Throw<DiscoverySkillDefinition>();
    }

    private sealed class InMemoryHiringRuntimeStore : IHiringRuntimeStore
    {
        private readonly Dictionary<string, HiringRuntimeContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

        public HiringRuntimeContext? Get(string hireId) =>
            _contexts.TryGetValue(hireId, out var context) ? context : null;

        public HiringRuntimeContext? GetBySessionId(string sessionId) =>
            _contexts.Values.FirstOrDefault(item =>
                string.Equals(item.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        public void Upsert(HiringRuntimeContext context) => _contexts[context.HireId] = context;
    }

    private sealed class NotSupportedKingCrabHttpClient : IKingCrabHttpClient
    {
        public Task<RemoteCallResult<T>> SendForJsonAsync<T>(HttpMethod method, string path, object? body, string ownerSubject, CancellationToken cancellationToken, bool useHireBotApiPrefix = true, string? absoluteBaseUrl = null, IReadOnlyDictionary<string, string>? additionalHeaders = null) => Throw<RemoteCallResult<T>>();
        public Task<RemoteCallResult<T>> SendMultipartForJsonAsync<T>(string path, string formFieldName, string fileName, byte[] content, string contentType, string ownerSubject, CancellationToken cancellationToken, bool useHireBotApiPrefix = false, string? absoluteBaseUrl = null, IReadOnlyDictionary<string, string>? additionalHeaders = null) => Throw<RemoteCallResult<T>>();
        public Task<RemoteBinaryCallResult> SendForBinaryAsync(HttpMethod method, string path, object? body, string ownerSubject, CancellationToken cancellationToken, bool useHireBotApiPrefix = true, string? absoluteBaseUrl = null, IReadOnlyDictionary<string, string>? additionalHeaders = null) => Throw<RemoteBinaryCallResult>();
    }

    private sealed class NotSupportedServiceScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    private sealed class NotSupportedHiringFileStore : IHiringFileStore
    {
        public Task<string> SaveAsync(string sessionId, string category, string fileName, Stream content, CancellationToken cancellationToken = default) => Throw<string>();
        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) => Throw<Stream>();
        public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default) => Throw<bool>();
    }

    private sealed class NotSupportedInstanceArtifactCloneService : IInstanceArtifactCloneService
    {
        public Task<InstanceArtifactCloneResult> CloneArtifactsAsync(EmployeeDetailDto source, string targetInstanceId, CancellationToken cancellationToken = default) => Throw<InstanceArtifactCloneResult>();
        public Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(string departmentInstanceId, IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken = default) => Throw<InstanceArtifactCloneResult>();
    }

    private sealed class NoOpInstanceArtifactCloneService : IInstanceArtifactCloneService
    {
        public Task<InstanceArtifactCloneResult> CloneArtifactsAsync(EmployeeDetailDto source, string targetInstanceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceArtifactCloneResult("v_test", targetInstanceId, []));

        public Task<InstanceArtifactCloneResult> StoreDepartmentArtifactsAsync(string departmentInstanceId, IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceArtifactCloneResult("v_test", departmentInstanceId, files.Keys.ToArray()));
    }

    private sealed class NotSupportedHiringArtifactPackageService : IHiringArtifactPackageService
    {
        public Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default) => Throw<HiringArtifactPackageSnapshotDto>();
        public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default) => Throw<HiringArtifactPackageSnapshotDto>();
        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(string hireId, CancellationToken cancellationToken = default) => Throw<HiringArtifactPackageSnapshotDto?>();
        public Task<HiringArtifactPackageSnapshotDto?> GetPackageByKindAsync(string hireId, string kind, CancellationToken cancellationToken = default) => Throw<HiringArtifactPackageSnapshotDto?>();
        public Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(string hireId, CancellationToken cancellationToken = default) => Throw<HiringArtifactDownloadResult>();
        public Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default) => Throw<HiringArtifactDownloadResult>();
    }

    private sealed class NotSupportedStoreSkillPackageDownloader : IStoreSkillPackageDownloader
    {
        public Task<IReadOnlyDictionary<string, byte[]>> DownloadSkillsAsync(IReadOnlyList<string> skillIds, CancellationToken cancellationToken = default) => Throw<IReadOnlyDictionary<string, byte[]>>();
    }

    internal sealed class StubStoreSkillPackageDownloader(IReadOnlyDictionary<string, byte[]> files) : IStoreSkillPackageDownloader
    {
        public Task<IReadOnlyDictionary<string, byte[]>> DownloadSkillsAsync(
            IReadOnlyList<string> skillIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(files);
    }

    /// <summary>测试用：明文往返，满足 develop 合并后 EmployeeHiringService 对 ISecretProtector 的依赖。</summary>
    private sealed class PassThroughSecretProtector : ISecretProtector
    {
        public string? Protect(string? value) => value;
        public string? Unprotect(string? value) => value;
    }

    private static Task<T> Throw<T>() => throw new NotSupportedException();
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
