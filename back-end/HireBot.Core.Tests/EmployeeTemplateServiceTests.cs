using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.EmployeeTemplate;
using HireBot.Core.Services.Evaluation;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class EmployeeTemplateServiceTests
{
    [Fact]
    public async Task GetTemplateDetailAsync_ShouldIncludeSkillsFromTargetTemplatePackage()
    {
        var template = new EmployeeTemplateDefinition(
            TemplateId: "sales-coach",
            IconUrl: "https://example.com/icon.png",
            Name: "销售教练",
            Tagline: "帮助销售梳理机会",
            Description: "desc",
            DetailDoc: "doc",
            CoreAbilityTags: ["sales"],
            HiredCount: 1,
            SuccessRate: 0m,
            AvgRating: 0m,
            IsAvailable: true,
            CoreAbilities: ["机会判断"],
            InScope: ["销售"],
            OutOfScope: [],
            Prerequisites: [],
            SuccessCases: []);
        var package = new TemplatePackageDefinition(
            RequestedTemplateId: "sales-coach",
            PackageId: "sales-coach",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            SourceArchive: null,
            PackageRootPath: "pkg-root",
            ManifestJson: "{}",
            DisplayName: "sales-coach",
            Description: "desc",
            PackageFiles: [],
            OntologySlices: [],
            Skills:
            [
                new TemplateSkillAsset("pipeline-qualifier", "skills/pipeline-qualifier/SKILL.md", true, "# skill", "h1"),
                new TemplateSkillAsset("bridge-to-forge", "skills/bridge-to-forge/SKILL.md", false, "# skill", "h2")
            ],
            RequiredSkills:
            [
                new TemplateSkillAsset("pipeline-qualifier", "skills/pipeline-qualifier/SKILL.md", true, "# skill", "h1")
            ],
            EntrySkill: "skills/pipeline-qualifier",
            StageRules: []);

        var service = new EmployeeTemplateService(
            new StubTemplateDataProvider(template),
            new StubTemplatePackageProvider(package),
            NullLogger<EmployeeTemplateService>.Instance);

        var result = await service.GetTemplateDetailAsync("sales-coach");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.PackageSkills.Count);
        Assert.Contains(result.Data.PackageSkills, skill => skill.Name == "pipeline-qualifier" && skill.Required);
        Assert.Contains(result.Data.PackageSkills, skill => skill.Name == "bridge-to-forge" && !skill.Required);
    }

    [Fact]
    public async Task FileSystemTemplatePackageProvider_ShouldKeepOptionalSkillsInManifest()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hirebot-template-package-tests", Guid.NewGuid().ToString("N"));
        var packageRoot = Path.Combine(tempRoot, "default", "NCrewTemplate");
        Directory.CreateDirectory(Path.Combine(packageRoot, "skills", "bridge-to-forge"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "skills", "context-priming"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, "manifest.json"),
                """
                {
                  "name": "default",
                  "skills": [
                    { "name": "bridge-to-forge", "path": "skills/bridge-to-forge", "required": false },
                    { "name": "context-priming", "path": "skills/context-priming", "required": true }
                  ]
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "skills", "bridge-to-forge", "SKILL.md"), "optional");
            await File.WriteAllTextAsync(Path.Combine(packageRoot, "skills", "context-priming", "SKILL.md"), "required");

            var provider = new FileSystemTemplatePackageProvider();

            var package = await provider.LoadFromDirectoryAsync(Path.Combine(tempRoot, "default"), "default");

            Assert.True(package.Skills.Count > package.RequiredSkills.Count);
            Assert.Contains(package.Skills, skill => skill.Name == "bridge-to-forge" && !skill.Required);
            Assert.Contains(package.RequiredSkills, skill => skill.Name == "context-priming" && skill.Required);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileSystemTemplatePackageProvider_ShouldExposeEvaluationConsumerAsEvaluationEntry()
    {
        var provider = new FileSystemTemplatePackageProvider();
        var packageRoot = Path.Combine(
            FindBackendRoot(),
            "HireBot.ApiService",
            "Assets",
            "DigitalEmployeeTemplates",
            "evaluation-expert");

        var package = await provider.LoadFromDirectoryAsync(packageRoot, "evaluation-expert");

        Assert.Equal("skills/evaluation-expert-consumer", package.EntrySkill);
        Assert.Contains(package.RequiredSkills, skill => skill.Name == "evaluation-expert-consumer");
        Assert.DoesNotContain(package.Skills, skill => skill.Name == "live_evaluation_coordinator");

        var archiveBytes = TemplatePackageArchiveBuilder.BuildArchive(package);
        using var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entries = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("skills/evaluation-expert-consumer/SKILL.md", entries);
        Assert.Contains("skills/evaluation-expert-consumer/runtime-drivers/ws_jwt/run.py", entries);
        Assert.Contains("skills/evaluation-expert-consumer/simulators/customer_realistic/simulator.json", entries);
        Assert.Contains("skills/evaluation-expert-consumer/metrics/factual_accuracy.metric.json", entries);
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/live_evaluation_coordinator/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/live_evaluator/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/evaluation_orchestrator/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/evaluator/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/report_generator/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/scenario_parser/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/test_executor/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.StartsWith("skills/training_advisor/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluationService_ShouldBuildConsumerRuntimeContextAndBootstrapPayload()
    {
        using var dbContext = CreateDbContext();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Evaluation:GatewayUseTls"] = "true",
                ["Evaluation:ConsumerGlobalTurnCap"] = "7",
                ["Evaluation:ApiBaseUrl"] = "http://hirebot.local"
            })
            .Build();
        var service = CreateEvaluationService(dbContext, configuration);
        var employee = CreateEvaluationEmployee();
        var ctx = CreateEvaluationWorkspaceContext();
        var session = CreateEvaluationSession("Eval Session/ABC", iteration: 2);

        var runtimeContextJson = (string)InvokeEvaluationPrivate(
            service,
            "BuildRuntimeContextJson",
            employee,
            ctx,
            session,
            "target.example/ws",
            "/workspace/uploads/evaluation-expert-consumer")!;

        using var contextDoc = JsonDocument.Parse(runtimeContextJson);
        var contextRoot = contextDoc.RootElement;
        Assert.Equal("ws_jwt", contextRoot.GetProperty("runtime_driver").GetProperty("driver_id").GetString());
        // token 字段已移除：driver 通过 hirebot_api.auth (client_credentials) 自主换取，不再注入静态 token
        Assert.False(contextRoot.GetProperty("runtime_driver").GetProperty("driver_config").TryGetProperty("token", out _));
        Assert.Equal("wss://target.example/ws", contextRoot.GetProperty("runtime_driver").GetProperty("driver_config").GetProperty("endpoint").GetString());
        Assert.Equal("customer_realistic", contextRoot.GetProperty("runtime_simulator").GetProperty("simulator_id").GetString());
        // 测试配置无 KingCrab 凭据，hirebot_api.auth 应缺失
        Assert.False(contextRoot.GetProperty("hirebot_api").TryGetProperty("auth", out _));
        // employee_provenance 枚举值必须符合 evaluation_context.schema.json (K17)
        var provenance = contextRoot.GetProperty("employee").GetProperty("employee_provenance");
        Assert.Equal("inferred_fallback", provenance.GetProperty("source").GetString());
        Assert.Equal("low", provenance.GetProperty("reliability").GetString());
        Assert.Equal(7, contextRoot.GetProperty("global_turn_cap").GetInt32());
        Assert.Equal(
            "/workspace/uploads/evaluation-expert-consumer/test-cases",
            contextRoot.GetProperty("paths").GetProperty("test_cases_dir").GetString());
        Assert.Equal(
            "/workspace/uploads/evaluation-expert-consumer/ontology",
            contextRoot.GetProperty("materials").GetProperty("ontology_dir").GetString());
        Assert.Equal(
            "/workspace/uploads/evaluation-expert-consumer/runs/eval-session-abc",
            contextRoot.GetProperty("paths").GetProperty("run_dir").GetString());
        Assert.Equal(
            "/workspace/uploads/evaluation-expert-consumer/runs/eval-session-abc/synthesized-cases",
            contextRoot.GetProperty("paths").GetProperty("synthesized_cases_dir").GetString());
        AssertDoesNotReferenceLegacyEvaluationFlow(runtimeContextJson);

        var bootstrapJson = (string)InvokeEvaluationStaticPrivate(
            "BuildLiveEvaluationBootstrapPayload",
            "owner-1",
            employee,
            ctx,
            session,
            "target.example/ws",
            "/workspace/runtime/evaluation-context.json")!;

        using var bootstrapDoc = JsonDocument.Parse(bootstrapJson);
        var bootstrapRoot = bootstrapDoc.RootElement;
        Assert.Equal("evaluation_consumer", bootstrapRoot.GetProperty("workflow").GetString());
        Assert.Equal("evaluation-expert-consumer", bootstrapRoot.GetProperty("skill_name").GetString());
        Assert.Contains("runtime_driver.driver_config", bootstrapRoot.GetProperty("instruction").GetString(), StringComparison.Ordinal);
        Assert.Contains("paths.run_dir/reports/evaluation_report.json", bootstrapRoot.GetProperty("instruction").GetString(), StringComparison.Ordinal);
        Assert.Contains("paths.run_dir/traces/<test_case_id>.trace.json", bootstrapRoot.GetProperty("instruction").GetString(), StringComparison.Ordinal);
        Assert.Contains("runtime-drivers/ws_jwt/requirements.txt", bootstrapRoot.GetProperty("instruction").GetString(), StringComparison.Ordinal);
        Assert.Contains("never create read_one_event.py", bootstrapRoot.GetProperty("instruction").GetString(), StringComparison.Ordinal);
        Assert.Contains("STEP 10 is the completion gate", bootstrapRoot.GetProperty("instruction").GetString(), StringComparison.Ordinal);
        AssertDoesNotReferenceLegacyEvaluationFlow(bootstrapJson);
    }

    [Fact]
    public async Task EvaluationService_ShouldUploadConsumerReadableMaterialsArchive()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hirebot-eval-consumer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var evaluatorTemplateZipPath = Path.Combine(tempRoot, "evaluator-template.zip");
            CreateEvaluatorTemplateZip(evaluatorTemplateZipPath);

            await using var dbContext = CreateDbContext();
            var sandbox = new CapturingSandboxService();
            var service = CreateEvaluationService(dbContext, new ConfigurationBuilder().Build(), sandbox);
            var employee = CreateEvaluationEmployee();
            var ctx = CreateEvaluationWorkspaceContext(evaluatorTemplateZipPath);

            var result = await InvokeEvaluationPrivateAsync<ApiResponse<string>>(
                service,
                "PrepareEvaluatorMaterialsArchiveAsync",
                "owner-1",
                employee,
                ctx,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal("/workspace/uploads/evaluation-expert-consumer", result.Data);

            var upload = Assert.Single(sandbox.WorkspaceUploads);
            Assert.Equal("uploads/evaluation-expert-consumer", upload.TargetDir);
            Assert.Equal("materials.zip", upload.FileName);
            Assert.Equal("application/zip", upload.ContentType);

            using var archive = new ZipArchive(new MemoryStream(upload.Content), ZipArchiveMode.Read);
            var archiveEntries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("testcases.json", archiveEntries);
            Assert.Contains("test-cases/greeting-case.tc.json", archiveEntries);
            Assert.Contains("ontology/ontology.json", archiveEntries);

            using var testCaseDoc = JsonDocument.Parse(ReadZipEntry(archive, "test-cases/greeting-case.tc.json"));
            var testCaseRoot = testCaseDoc.RootElement;
            Assert.Equal("greeting-case", testCaseRoot.GetProperty("test_case_id").GetString());
            Assert.Equal(
                "Customer asks about warranty coverage.",
                testCaseRoot.GetProperty("input").GetProperty("opening_message").GetString());
            Assert.Equal(
                "Explain the warranty path.",
                testCaseRoot.GetProperty("expected_output").GetProperty("expected_outcomes")[0].GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvaluationService_ShouldKeepConsumerTestCasesEmptyWhenNoBusinessTestcases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hirebot-eval-empty-cases-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var evaluatorTemplateZipPath = Path.Combine(tempRoot, "evaluator-template.zip");
            CreateEvaluatorTemplateZipWithoutTestcases(evaluatorTemplateZipPath);

            await using var dbContext = CreateDbContext();
            var sandbox = new CapturingSandboxService();
            var service = CreateEvaluationService(dbContext, new ConfigurationBuilder().Build(), sandbox);
            var employee = CreateEvaluationEmployee();
            var ctx = CreateEvaluationWorkspaceContext(evaluatorTemplateZipPath);

            var result = await InvokeEvaluationPrivateAsync<ApiResponse<string>>(
                service,
                "PrepareEvaluatorMaterialsArchiveAsync",
                "owner-1",
                employee,
                ctx,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            var upload = Assert.Single(sandbox.WorkspaceUploads);

            using var archive = new ZipArchive(new MemoryStream(upload.Content), ZipArchiveMode.Read);
            var archiveEntries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("testcases.json", archiveEntries);
            Assert.Contains("test-cases/", archiveEntries);
            Assert.DoesNotContain(archiveEntries, entry =>
                entry.StartsWith("test-cases/", StringComparison.OrdinalIgnoreCase) &&
                entry.EndsWith(".tc.json", StringComparison.OrdinalIgnoreCase));

            using var indexDoc = JsonDocument.Parse(ReadZipEntry(archive, "testcases.json"));
            Assert.Equal(JsonValueKind.Array, indexDoc.RootElement.ValueKind);
            Assert.Empty(indexDoc.RootElement.EnumerateArray());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvaluationService_ShouldUseFinalArtifactPackageTestcasesAndIgnoreIntermediate()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hirebot-eval-final-cases-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var evaluatorTemplateZipPath = Path.Combine(tempRoot, "evaluator-template.zip");
            CreateEvaluatorTemplateZipWithoutTestcases(evaluatorTemplateZipPath);

            var finalArchive = BuildZipArchiveBytes(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["testcases/evaluation-test-cases.json"] = Encoding.UTF8.GetBytes(
                    """
                    {
                      "cases": [
                        {
                          "case_id": "artifact-case-001",
                          "title": "Final artifact generated testcase",
                          "input": {
                            "user_request": "Final artifact customer asks for access approval."
                          }
                        }
                      ]
                    }
                    """)
            });
            var intermediateArchive = BuildZipArchiveBytes(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["testcases/evaluation-test-cases.json"] = Encoding.UTF8.GetBytes(
                    """
                    {
                      "cases": [
                        {
                          "case_id": "intermediate-case-001",
                          "title": "Intermediate testcase should be ignored",
                          "input": {
                            "user_request": "Intermediate draft request."
                          }
                        }
                      ]
                    }
                    """)
            });

            await using var dbContext = CreateDbContext();
            var sandbox = new CapturingSandboxService();
            var artifactPackageService = new FixedArtifactPackageService(finalArchive, intermediateArchive);
            var service = CreateEvaluationService(
                dbContext,
                new ConfigurationBuilder().Build(),
                sandbox,
                artifactPackageService);
            var employee = CreateEvaluationEmployee();
            var ctx = CreateEvaluationWorkspaceContext(evaluatorTemplateZipPath);

            var result = await InvokeEvaluationPrivateAsync<ApiResponse<string>>(
                service,
                "PrepareEvaluatorMaterialsArchiveAsync",
                "owner-1",
                employee,
                ctx,
                CancellationToken.None);

            Assert.True(result.Success, result.Message);
            var upload = Assert.Single(sandbox.WorkspaceUploads);

            using var archive = new ZipArchive(new MemoryStream(upload.Content), ZipArchiveMode.Read);
            var archiveEntries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("test-cases/artifact-case-001.tc.json", archiveEntries);
            Assert.DoesNotContain("test-cases/intermediate-case-001.tc.json", archiveEntries);

            using var testCaseDoc = JsonDocument.Parse(ReadZipEntry(archive, "test-cases/artifact-case-001.tc.json"));
            var testCaseRoot = testCaseDoc.RootElement;
            Assert.Equal("artifact-case-001", testCaseRoot.GetProperty("test_case_id").GetString());
            Assert.Equal(
                "Final artifact customer asks for access approval.",
                testCaseRoot.GetProperty("input").GetProperty("opening_message").GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvaluationService_ShouldPersistQuestionCardsFromInitializedMaterials()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hirebot-eval-card-materials-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var evaluatorTemplateZipPath = Path.Combine(tempRoot, "evaluator-template.zip");
            CreateEvaluatorTemplateZip(evaluatorTemplateZipPath);

            await using var dbContext = CreateDbContext();
            var assetRoot = Path.Combine(tempRoot, "eval-assets");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HireBot:EvaluationResourceRoot"] = assetRoot
                })
                .Build();
            var service = CreateEvaluationService(
                dbContext,
                configuration,
                assetStore: new FileBackedEvaluationAssetStore(assetRoot));
            var employee = CreateEvaluationEmployee();
            var ctx = CreateEvaluationWorkspaceContext(evaluatorTemplateZipPath);
            var session = CreateEvaluationSession("eval-card-materials", iteration: 1);
            dbContext.EvaluationSessions.Add(session);
            await dbContext.SaveChangesAsync();

            var cards = await InvokeEvaluationPrivateAsync<IReadOnlyList<EvaluationQuestionCardDto>>(
                service,
                "EnsureQuestionCardsForSessionAsync",
                session,
                ctx,
                employee,
                CancellationToken.None);

            var card = Assert.Single(cards);
            Assert.Equal("Greeting Case", card.TestcaseId);
            Assert.Equal("Warranty greeting", card.Title);
            Assert.Contains(dbContext.EvaluationAssets, asset =>
                asset.SessionEntityId == session.Id &&
                asset.AssetType == "testcases-json" &&
                asset.SourceType == "evaluator-template-package");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvaluationService_ShouldPersistQuestionCardsFromFinalArtifactPackage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hirebot-eval-card-final-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var evaluatorTemplateZipPath = Path.Combine(tempRoot, "evaluator-template.zip");
            CreateEvaluatorTemplateZipWithoutTestcases(evaluatorTemplateZipPath);
            var finalArchive = BuildZipArchiveBytes(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["testcases/evaluation-test-cases.json"] = Encoding.UTF8.GetBytes(
                    """
                    {
                      "cases": [
                        {
                          "case_id": "final-card-case",
                          "title": "Final generated testcase card",
                          "input": {
                            "user_request": "Generated final package request."
                          }
                        }
                      ]
                    }
                    """)
            });
            var intermediateArchive = BuildZipArchiveBytes(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["testcases/evaluation-test-cases.json"] = Encoding.UTF8.GetBytes(
                    """
                    {
                      "cases": [
                        {
                          "case_id": "draft-card-case",
                          "title": "Draft testcase card",
                          "input": {
                            "user_request": "Draft request."
                          }
                        }
                      ]
                    }
                    """)
            });

            await using var dbContext = CreateDbContext();
            var assetRoot = Path.Combine(tempRoot, "eval-assets");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HireBot:EvaluationResourceRoot"] = assetRoot
                })
                .Build();
            var service = CreateEvaluationService(
                dbContext,
                configuration,
                artifactPackageService: new FixedArtifactPackageService(finalArchive, intermediateArchive),
                assetStore: new FileBackedEvaluationAssetStore(assetRoot));
            var employee = CreateEvaluationEmployee();
            var ctx = CreateEvaluationWorkspaceContext(evaluatorTemplateZipPath);
            var session = CreateEvaluationSession("eval-card-final", iteration: 1);
            dbContext.EvaluationSessions.Add(session);
            await dbContext.SaveChangesAsync();

            var cards = await InvokeEvaluationPrivateAsync<IReadOnlyList<EvaluationQuestionCardDto>>(
                service,
                "EnsureQuestionCardsForSessionAsync",
                session,
                ctx,
                employee,
                CancellationToken.None);

            var card = Assert.Single(cards);
            Assert.Equal("final-card-case", card.TestcaseId);
            Assert.Equal("Final generated testcase card", card.Title);
            Assert.DoesNotContain(cards, item => item.TestcaseId == "draft-card-case");
            Assert.Contains(dbContext.EvaluationAssets, asset =>
                asset.SessionEntityId == session.Id &&
                asset.AssetType == "testcases-json" &&
                asset.SourceType == $"artifact-package:{HiringArtifactPackageKinds.FinalPackageZip}");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvaluationService_ShouldPersistQuestionCardsFromConversationGeneratedTestcases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hirebot-eval-card-conversation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await using var dbContext = CreateDbContext();
            var assetRoot = Path.Combine(tempRoot, "eval-assets");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HireBot:EvaluationResourceRoot"] = assetRoot
                })
                .Build();
            var service = CreateEvaluationService(
                dbContext,
                configuration,
                assetStore: new FileBackedEvaluationAssetStore(assetRoot));
            var session = CreateEvaluationSession("eval-card-conversation", iteration: 1);
            dbContext.EvaluationSessions.Add(session);
            await dbContext.SaveChangesAsync();
            var messages = new[]
            {
                new HiringConversationMessageDto(
                    "assistant-1",
                    "assistant",
                    """
                    已生成评估测试用例：
                    ```json
                    {
                      "test_cases": [
                        {
                          "test_case_id": "conversation-generated-case",
                          "scenario_name": "Conversation generated testcase",
                          "input": {
                            "opening_message": "Generated from evaluation conversation."
                          }
                        }
                      ]
                    }
                    ```
                    """,
                    DateTimeOffset.UtcNow)
            };

            var cards = await InvokeEvaluationPrivateAsync<IReadOnlyList<EvaluationQuestionCardDto>>(
                service,
                "EnsureQuestionCardsFromConversationAsync",
                session,
                messages,
                CancellationToken.None);

            var card = Assert.Single(cards);
            Assert.Equal("conversation-generated-case", card.TestcaseId);
            Assert.Equal("Conversation generated testcase", card.Title);
            Assert.Equal("Generated from evaluation conversation.", card.Prompt);
            Assert.Contains(dbContext.EvaluationAssets, asset =>
                asset.SessionEntityId == session.Id &&
                asset.AssetType == "testcases-json" &&
                asset.SourceType == "evaluator-conversation");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvaluationService_ShouldPersistQuestionCardsFromTraceGeneratedTestcases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hirebot-eval-card-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await using var dbContext = CreateDbContext();
            var assetRoot = Path.Combine(tempRoot, "eval-assets");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HireBot:EvaluationResourceRoot"] = assetRoot
                })
                .Build();
            var service = CreateEvaluationService(
                dbContext,
                configuration,
                assetStore: new FileBackedEvaluationAssetStore(assetRoot));
            var session = CreateEvaluationSession("eval-card-trace", iteration: 1);
            dbContext.EvaluationSessions.Add(session);
            await dbContext.SaveChangesAsync();
            const string traceJson =
                """
                {
                  "events": [
                    {
                      "type": "step_1_5_synthesized",
                      "payload": {
                        "cases": [
                          {
                            "case_id": "trace-generated-case",
                            "title": "Trace generated testcase",
                            "input": {
                              "user_message": "Generated from trace sync."
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
                """;

            var cards = await InvokeEvaluationPrivateAsync<IReadOnlyList<EvaluationQuestionCardDto>>(
                service,
                "EnsureQuestionCardsFromRuntimeTextAsync",
                session,
                "trace-testcases-eval-card-trace.json",
                "evaluator-trace",
                traceJson,
                CancellationToken.None);

            var card = Assert.Single(cards);
            Assert.Equal("trace-generated-case", card.TestcaseId);
            Assert.Equal("Trace generated testcase", card.Title);
            Assert.Equal("Generated from trace sync.", card.Prompt);
            Assert.Contains(dbContext.EvaluationAssets, asset =>
                asset.SessionEntityId == session.Id &&
                asset.AssetType == "testcases-json" &&
                asset.SourceType == "evaluator-trace");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string FindBackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "HireBot.ApiService", "Assets", "DigitalEmployeeTemplates");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate back-end root from test base directory.");
    }

    private static EvaluationService CreateEvaluationService(
        HireBotDbContext dbContext,
        IConfiguration configuration,
        ISandboxService? sandbox = null,
        IHiringArtifactPackageService? artifactPackageService = null,
        IEvaluationAssetStore? assetStore = null)
    {
        var hostEnvironment = new TestHostingEnvironment();
        return new EvaluationService(
            artifactPackageService ?? new EmptyArtifactPackageService(),
            sandbox ?? new CapturingSandboxService(),
            new TestUserIdentity("owner-1", "tenant-1", "operator-1"),
            dbContext,
            assetStore ?? new ThrowingEvaluationAssetStore(),
            null!,
            hostEnvironment,
            configuration,
            NullLogger<EvaluationService>.Instance,
            new KingCrabSandboxTokenProvider(
                new TestHttpClientFactory(),
                configuration,
                NullLogger<KingCrabSandboxTokenProvider>.Instance),
            new ThrowingEvaluationTemplatePackageProvider(),
            new FileSystemTemplatePackageProvider());
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private static EmployeeDetailDto CreateEvaluationEmployee()
    {
        return new EmployeeDetailDto(
            EmployeeId: "emp-consumer-1",
            Nickname: "Consumer Agent",
            RoleName: "customer_service",
            SourceTemplate: "consumer_support",
            SourceTemplateId: "consumer-support-template",
            InstanceType: "personal_clone",
            Status: "ready",
            BasedOnTemplateId: null,
            FromInstanceId: null,
            OwnerUserId: "owner-1",
            DepartmentId: "dept-1",
            LifecycleStatus: "active",
            StageSummary: "Handles customer requests",
            PrimarySignal: "ready",
            SignalLevel: "normal",
            OwningTeam: "support",
            CreatedAt: DateTimeOffset.UtcNow,
            InternshipStartAt: null,
            GraduatedAt: null,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: Array.Empty<string>(),
            Capabilities: Array.Empty<EmployeeCapabilityDto>(),
            EvalPhase: null,
            EvalIteration: null,
            EvalMaxIterations: null,
            IsConfigured: true,
            CardIntro: "Support employee");
    }

    private static EvaluationSessionEntity CreateEvaluationSession(string sessionId, int iteration)
    {
        return new EvaluationSessionEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            OwnerSubject = "owner-1",
            TenantId = "owner-1",
            EmployeeId = "emp-consumer-1",
            TargetHireId = "target-hire-1",
            TargetSandboxId = "target-sandbox-1",
            EvaluatorHireId = "evaluator-hire-1",
            EvaluatorSandboxId = "evaluator-sandbox-1",
            Iteration = iteration,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static object CreateEvaluationWorkspaceContext(string? evaluatorTemplateZipPath = null)
    {
        var contextType = GetEvaluationServiceNestedType("EvaluationWorkspaceContext");
        var stepStateType = GetEvaluationServiceNestedType("WorkspaceStepState");
        var stepStatesType = typeof(Dictionary<,>).MakeGenericType(typeof(string), stepStateType);
        var stepStates = Activator.CreateInstance(stepStatesType)!;

        return Activator.CreateInstance(
            contextType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                "target-hire-1",
                "target-sandbox-1",
                "evaluator-hire-1",
                "evaluator-sandbox-1",
                DateTimeOffset.UtcNow,
                "session-1",
                evaluatorTemplateZipPath,
                null,
                null,
                stepStates,
                null
            ],
            culture: null)!;
    }

    private static Type GetEvaluationServiceNestedType(string name)
    {
        return typeof(EvaluationService).GetNestedType(name, BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Missing nested type {name}.");
    }

    private static object? InvokeEvaluationPrivate(object instance, string methodName, params object?[] args)
    {
        var method = typeof(EvaluationService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException($"Missing method {methodName}.");
        return method.Invoke(instance, args);
    }

    private static object? InvokeEvaluationStaticPrivate(string methodName, params object?[] args)
    {
        var method = typeof(EvaluationService).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException($"Missing static method {methodName}.");
        return method.Invoke(null, args);
    }

    private static async Task<T> InvokeEvaluationPrivateAsync<T>(object instance, string methodName, params object?[] args)
    {
        var method = typeof(EvaluationService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException($"Missing method {methodName}.");
        var task = (Task<T>)method.Invoke(instance, args)!;
        return await task;
    }

    private static void CreateEvaluatorTemplateZip(string zipPath)
    {
        using var fileStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("testcases/consumer.json");
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(
            """
            {
              "test_cases": [
                {
                  "test_case_id": "Greeting Case",
                  "scenario_name": "Warranty greeting",
                  "input": {
                    "user_request": "Customer asks about warranty coverage.",
                    "context": {
                      "order_state": "delivered"
                    }
                  },
                  "expected_behavior_sequence": [
                    {
                      "criteria": "Acknowledge the customer request."
                    }
                  ],
                  "expected_output": {
                    "resolution": "Explain the warranty path.",
                    "user_satisfaction": "Customer knows the next step."
                  }
                }
              ]
            }
            """);
    }

    private static void CreateEvaluatorTemplateZipWithoutTestcases(string zipPath)
    {
        using var fileStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("ontology/notes.md");
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write("No business testcases are present in this package.");
    }

    private static byte[] BuildZipArchiveBytes(IReadOnlyDictionary<string, byte[]> files)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key);
                using var entryStream = entry.Open();
                entryStream.Write(file.Value);
            }
        }

        return memoryStream.ToArray();
    }

    private static string ReadZipEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"Missing zip entry {path}.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertDoesNotReferenceLegacyEvaluationFlow(string value)
    {
        Assert.DoesNotContain("live_evaluator", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("live_evaluation_coordinator", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evaluate.py --mode execute", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uploads/materials", value, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubTemplateDataProvider(EmployeeTemplateDefinition template) : ITemplateDataProvider
    {
        public Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmployeeTemplateDefinition>>([template]);

        public Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
            => Task.FromResult<EmployeeTemplateDefinition?>(template);
    }

    private sealed class StubTemplatePackageProvider(TemplatePackageDefinition package) : ITemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default)
            => Task.FromResult(package);
    }

    private sealed class CapturingSandboxService : ISandboxService
    {
        public List<SandboxWorkspaceUploadRequestDto> WorkspaceUploads { get; } = [];

        public Task<ApiResponse<SandboxWorkspaceUploadResultDto>> UploadWorkspaceFileAsync(
            SandboxWorkspaceUploadRequestDto request,
            CancellationToken cancellationToken = default)
        {
            WorkspaceUploads.Add(request);
            var workspaceDir = $"/workspace/{request.TargetDir.Trim('/')}";
            var result = new SandboxWorkspaceUploadResultDto(
                Files: Array.Empty<string>(),
                FileCount: 0,
                WorkspaceDir: workspaceDir);
            return Task.FromResult(ApiResponse<SandboxWorkspaceUploadResultDto>.SuccessResponse(result));
        }

        public Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(SandboxRegisterRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> CreateAsync(SandboxCreateRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<bool>> DeleteAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<IReadOnlyList<SandboxInstanceDto>>> ListByOwnerAsync(string ownerSubject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<bool>> DeleteForOwnerAsync(string sandboxId, string ownerSubject, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(SandboxEnsureSessionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(SandboxSendMessageRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(SandboxTimelineRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(SandboxSessionDetailRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(SandboxAttachmentUploadRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<DigitalEmployeeTemplateUploadResultDto>> UploadDigitalEmployeeTemplateAsync(DigitalEmployeeTemplateUploadRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyArtifactPackageService : IHiringArtifactPackageService
    {
        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(string hireId, CancellationToken cancellationToken = default)
            => Task.FromResult<HiringArtifactPackageSnapshotDto?>(null);

        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageByEmployeeIdAsync(string employeeId, CancellationToken cancellationToken = default)
            => Task.FromResult<HiringArtifactPackageSnapshotDto?>(null);

        public Task<HiringArtifactPackageSnapshotDto?> GetPackageByKindAsync(string hireId, string kind, CancellationToken cancellationToken = default)
            => Task.FromResult<HiringArtifactPackageSnapshotDto?>(null);

        public Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(string hireId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedArtifactPackageService(byte[] finalArchive, byte[] intermediateArchive) : IHiringArtifactPackageService
    {
        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(string hireId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<HiringArtifactPackageSnapshotDto?>(new HiringArtifactPackageSnapshotDto(
                hireId,
                "session-test",
                HiringArtifactPackageKinds.IntermediatePackageZip,
                $"{hireId}-intermediate.zip",
                "packages/intermediate.zip",
                "sha-intermediate",
                intermediateArchive,
                false));
        }

        public Task<HiringArtifactPackageSnapshotDto?> GetPackageByKindAsync(
            string hireId,
            string kind,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(kind, HiringArtifactPackageKinds.FinalPackageZip, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<HiringArtifactPackageSnapshotDto?>(null);
            }

            return Task.FromResult<HiringArtifactPackageSnapshotDto?>(new HiringArtifactPackageSnapshotDto(
                hireId,
                "session-test",
                HiringArtifactPackageKinds.FinalPackageZip,
                $"{hireId}-final.zip",
                "packages/final.zip",
                "sha-final",
                finalArchive,
                true));
        }

        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageByEmployeeIdAsync(string employeeId, CancellationToken cancellationToken = default)
            => Task.FromResult<HiringArtifactPackageSnapshotDto?>(null);

        public Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(HiringArtifactPackagePersistRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(string hireId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(string hireId, string artifactName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FileBackedEvaluationAssetStore(string root) : IEvaluationAssetStore
    {
        public Task<StoredEvaluationAsset> SaveTextAsync(
            string sessionId,
            int iteration,
            string assetType,
            string fileName,
            string content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            return SaveBytesAsync(
                sessionId,
                iteration,
                assetType,
                fileName,
                Encoding.UTF8.GetBytes(content),
                mimeType,
                cancellationToken);
        }

        public async Task<StoredEvaluationAsset> SaveBytesAsync(
            string sessionId,
            int iteration,
            string assetType,
            string fileName,
            byte[] content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = "asset.json";
            }

            var relativePath = Path.Combine(
                    "evaluation",
                    sessionId,
                    iteration.ToString(),
                    assetType,
                    safeFileName)
                .Replace('\\', '/');
            var physicalPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await File.WriteAllBytesAsync(physicalPath, content, cancellationToken);

            return new StoredEvaluationAsset(
                RelativePath: relativePath,
                PublicUrl: $"/test-assets/{relativePath}",
                MimeType: mimeType,
                Size: content.Length,
                ContentHash: Convert.ToHexStringLower(SHA256.HashData(content)),
                PhysicalPath: physicalPath);
        }
    }

    private sealed class ThrowingEvaluationAssetStore : IEvaluationAssetStore
    {
        public Task<StoredEvaluationAsset> SaveTextAsync(string sessionId, int iteration, string assetType, string fileName, string content, string mimeType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredEvaluationAsset> SaveBytesAsync(string sessionId, int iteration, string assetType, string fileName, byte[] content, string mimeType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingEvaluationTemplatePackageProvider : ITemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestUserIdentity(string ownerSubject, string tenantId, string operatorId) : HireBot.Abstraction.Infrastructure.Identity.IUserIdentity
    {
        public string Id => ownerSubject;
        public string Email => "test@example.com";
        public string UserName => "testuser";
        public string FirstName => "Test";
        public string LastName => "User";
        public string FullName => "Test User";
        public string DisplayName => "Test User";
        public string? TenantId => tenantId;
        public string? TenantName => "Test Tenant";
        public string OperatorId => operatorId;
        public string OwnerSubject => ownerSubject;
        public string? Role => "admin";
        public bool IsAuthenticated => true;
        public string? DepartmentId => null;
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private class ThrowingProxy<T> : DispatchProxy where T : class
    {
        public static T Create() => DispatchProxy.Create<T, ThrowingProxy<T>>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new NotSupportedException(targetMethod?.Name ?? typeof(T).Name);
        }
    }

    private sealed class TestHostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "HireBot.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
