using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Tests;

public class ImportPackageTestCasesTests
{
    [Fact]
    public async Task ImportPackageAsync_WhenSandboxZipHasNoTestcases_ShouldIncludeStagedTestcasesInFinal()
    {
        var artifactRecorder = new RecordingHiringArtifactPackageService();
        var context = CreateImportRuntimeContext();
        var sandbox = PackagingTestCasesFromHistoryTests.CreateSandboxFake(
            skillReply: PackagingTestCasesFromHistoryTests.BuildSkillSuccessReply(includeExtendedBundle: false));
        var service = EmployeeHiringServicePackagingTestFactory.Create(
            sandbox,
            context,
            artifactPackageService: artifactRecorder,
            templateDataProvider: new EmployeeHiringServicePackagingTestFactory.StubTemplateDataProvider());

        await using var packageStream = BuildSandboxPackageStream();
        var result = await service.ImportPackageAsync(
            context.HireId,
            packageStream,
            "sandbox-package.zip",
            cancellationToken: CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(artifactRecorder.FinalFiles);
        Assert.True(artifactRecorder.FinalFiles!.ContainsKey("testcases/evaluation-test-cases.json"));
        Assert.NotNull(artifactRecorder.IntermediateFiles);
        Assert.True(artifactRecorder.IntermediateFiles!.ContainsKey("testcases/evaluation-test-cases.json"));

        var finalJson = Encoding.UTF8.GetString(artifactRecorder.FinalFiles["testcases/evaluation-test-cases.json"]);
        Assert.Contains("TC-001", finalJson);
        Assert.Contains("TC-001", finalJson);
    }

    [Fact]
    public async Task ImportPackageAsync_WhenMaterialStageAndSandboxZipHasNoTestcases_ShouldIncludeStagedTestcasesInFinal()
    {
        var artifactRecorder = new RecordingHiringArtifactPackageService();
        var context = CreateImportRuntimeContext() with
        {
            CurrentStage = HiringCollectionStage.Material,
            PackagingTestCasesStaged = false
        };
        var sandbox = PackagingTestCasesFromHistoryTests.CreateSandboxFake(
            skillReply: PackagingTestCasesFromHistoryTests.BuildSkillSuccessReply(includeExtendedBundle: false));
        var service = EmployeeHiringServicePackagingTestFactory.Create(
            sandbox,
            context,
            artifactPackageService: artifactRecorder,
            templateDataProvider: new EmployeeHiringServicePackagingTestFactory.StubTemplateDataProvider());

        await using var packageStream = BuildSandboxPackageStream();
        var result = await service.ImportPackageAsync(
            context.HireId,
            packageStream,
            "sandbox-package.zip",
            cancellationToken: CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(artifactRecorder.FinalFiles);
        Assert.True(artifactRecorder.FinalFiles!.ContainsKey("testcases/evaluation-test-cases.json"));

        var finalJson = Encoding.UTF8.GetString(artifactRecorder.FinalFiles["testcases/evaluation-test-cases.json"]);
        Assert.Contains("TC-001", finalJson);
    }

    [Fact]
    public async Task ImportPackageAsync_WhenMergedAlreadyHasSkillGuidedFallback_ShouldMergeNotOverwrite()
    {
        var artifactRecorder = new RecordingHiringArtifactPackageService();
        var context = CreateImportRuntimeContext();
        var sandbox = PackagingTestCasesFromHistoryTests.CreateSandboxFake(
            skillReply: PackagingTestCasesFromHistoryTests.BuildSkillSuccessReply(includeExtendedBundle: false));

        var skillGuidedJson = """
            {
              "source": "conversation-skill-guided",
              "cases": [
                {
                  "caseId": "eval-case-001",
                  "title": "正常流程",
                  "objective": "验证闭环"
                }
              ]
            }
            """;

        var storeSkillFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["testcases/evaluation-test-cases.json"] = Encoding.UTF8.GetBytes(skillGuidedJson)
        };
        var service = EmployeeHiringServicePackagingTestFactory.Create(
            sandbox,
            context,
            artifactPackageService: artifactRecorder,
            templateDataProvider: new EmployeeHiringServicePackagingTestFactory.StubTemplateDataProvider(),
            storeSkillPackageDownloader: new EmployeeHiringServicePackagingTestFactory.StubStoreSkillPackageDownloader(storeSkillFiles));

        await using var packageStream = BuildSandboxPackageStream();
        var result = await service.ImportPackageAsync(
            context.HireId,
            packageStream,
            "sandbox-package.zip",
            linkedStoreSkillIds: ["store-skill-001"],
            cancellationToken: CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(artifactRecorder.FinalFiles);

        var finalJson = Encoding.UTF8.GetString(artifactRecorder.FinalFiles!["testcases/evaluation-test-cases.json"]);
        using var doc = JsonDocument.Parse(finalJson);
        var cases = doc.RootElement.GetProperty("test_cases");
        Assert.Equal(2, cases.GetArrayLength());
        Assert.Contains("TC-001", finalJson);
        Assert.Contains("eval-case-001", finalJson);
    }

    private static HiringRuntimeContext CreateImportRuntimeContext()
    {
        var templatePackage = new TemplatePackageDefinition(
            RequestedTemplateId: "employment-coach",
            PackageId: "pkg",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            SourceArchive: null,
            PackageRootPath: "Assets/TemplatePackages/default/NCrewTemplate",
            ManifestJson: "{\"name\":\"pkg\"}",
            DisplayName: "pkg",
            Description: "desc",
            PackageFiles:
            [
                new TemplatePackageFileAsset("manifest.json", Encoding.UTF8.GetBytes("{\"name\":\"pkg\"}"), "hash-manifest")
            ],
            OntologySlices: [],
            Skills: [],
            RequiredSkills: [],
            EntrySkill: null,
            StageRules: []);

        return new HiringRuntimeContext
        {
            HireId = "hire-packaging-import",
            SessionId = "session-import-001",
            SandboxId = "sandbox-import",
            TemplateId = "employment-coach",
            TemplateName = "雇佣教练",
            EmployeeId = "employee-import-001",
            OwnerSubject = "user-subject",
            TenantId = "tenant",
            OperatorId = "operator",
            CurrentStage = HiringCollectionStage.ReadyForPackaging,
            CollectionPhase = HiringCollectionPhase.InProgress,
            RoleTemplatePackage = templatePackage,
            WorkingTemplatePackage = templatePackage,
            DiscoverySkill = new DiscoverySkillDefinition(
                SkillId: "employment-coach-conversation",
                SkillVersion: "1.0.0",
                SkillHash: "hash",
                SkillRootPath: "Assets/DigitalEmployeeTemplates/employment-coach-conversation",
                SkillContent: "# discovery",
                Files: [],
                StageRules: []),
            PackagingTestCasesStaged = false
        };
    }

    private static MemoryStream BuildSandboxPackageStream(params (string Path, string Content)[] extraEntries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", "{\"name\":\"sandbox\"}");
            WriteEntry(archive, "skills/README.md", "# skills");
            foreach (var (path, content) in extraEntries)
            {
                WriteEntry(archive, path, content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        var bytes = Encoding.UTF8.GetBytes(content);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private sealed class RecordingHiringArtifactPackageService : IHiringArtifactPackageService
    {
        public IReadOnlyDictionary<string, byte[]>? IntermediateFiles { get; private set; }
        public IReadOnlyDictionary<string, byte[]>? FinalFiles { get; private set; }

        public Task<HiringArtifactPackageSnapshotDto> PersistIntermediatePackageAsync(
            HiringArtifactPackagePersistRequestDto request,
            CancellationToken cancellationToken = default)
        {
            IntermediateFiles = CloneFiles(request.Files);
            var archiveBytes = BuildArchive(IntermediateFiles);
            return Task.FromResult(CreateSnapshot(request, HiringArtifactPackageKinds.IntermediatePackageZip, archiveBytes));
        }

        public Task<HiringArtifactPackageSnapshotDto> PersistFinalPackageAsync(
            HiringArtifactPackagePersistRequestDto request,
            CancellationToken cancellationToken = default)
        {
            FinalFiles = CloneFiles(request.Files);
            var archiveBytes = BuildArchive(FinalFiles);
            return Task.FromResult(CreateSnapshot(request, HiringArtifactPackageKinds.FinalPackageZip, archiveBytes));
        }

        public Task<HiringArtifactPackageSnapshotDto?> GetLatestPackageAsync(string hireId, CancellationToken cancellationToken = default)
        {
            if (FinalFiles is not null)
            {
                return Task.FromResult<HiringArtifactPackageSnapshotDto?>(new HiringArtifactPackageSnapshotDto(
                    hireId,
                    "session-import-001",
                    HiringArtifactPackageKinds.FinalPackageZip,
                    $"{hireId}_final_package.zip",
                    "packages/final/package.zip",
                    "sha-final",
                    BuildArchive(FinalFiles),
                    true));
            }

            return Task.FromResult<HiringArtifactPackageSnapshotDto?>(null);
        }

        public Task<HiringArtifactPackageSnapshotDto?> GetPackageByKindAsync(
            string hireId,
            string kind,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(kind, HiringArtifactPackageKinds.IntermediatePackageZip, StringComparison.OrdinalIgnoreCase) &&
                IntermediateFiles is not null)
            {
                return Task.FromResult<HiringArtifactPackageSnapshotDto?>(new HiringArtifactPackageSnapshotDto(
                    hireId,
                    "session-import-001",
                    kind,
                    $"{hireId}_intermediate_package.zip",
                    "packages/intermediate/package.zip",
                    "sha-intermediate",
                    BuildArchive(IntermediateFiles),
                    false));
            }

            return GetLatestPackageAsync(hireId, cancellationToken);
        }

        public Task<HiringArtifactDownloadResult> BuildFinalPackageDownloadAsync(string hireId, CancellationToken cancellationToken = default)
        {
            if (FinalFiles is null)
            {
                return Task.FromResult(HiringArtifactDownloadResult.Error(409, "not found"));
            }

            return Task.FromResult(HiringArtifactDownloadResult.Success(
                $"{hireId}_final_package.zip",
                "application/zip",
                BuildArchive(FinalFiles)));
        }

        public Task<HiringArtifactDownloadResult> BuildFinalPackageFileDownloadAsync(
            string hireId,
            string artifactName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(404, "not supported"));
        }

        private static HiringArtifactPackageSnapshotDto CreateSnapshot(
            HiringArtifactPackagePersistRequestDto request,
            string kind,
            byte[] archiveBytes)
        {
            return new HiringArtifactPackageSnapshotDto(
                request.HireId,
                request.SessionId,
                kind,
                request.FileName,
                kind == HiringArtifactPackageKinds.FinalPackageZip
                    ? "packages/final/package.zip"
                    : "packages/intermediate/package.zip",
                "sha256-test",
                archiveBytes,
                kind == HiringArtifactPackageKinds.FinalPackageZip);
        }

        private static Dictionary<string, byte[]> CloneFiles(IReadOnlyDictionary<string, byte[]> files)
        {
            return files.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }

        private static byte[] BuildArchive(IReadOnlyDictionary<string, byte[]> files)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var pair in files)
                {
                    var entry = archive.CreateEntry(pair.Key, CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    entryStream.Write(pair.Value, 0, pair.Value.Length);
                }
            }

            return stream.ToArray();
        }
    }
}
