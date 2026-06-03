using System.IO.Compression;
using System.Text;
using HireBot.Core.Services.Hiring;

namespace HireBot.Core.Tests;

public class FinalPackageZipAcceptanceTests
{
    [Fact]
    public void FinalPackageZip_ShouldContainValidPackagingTestCases_WhenEnvZipProvided()
    {
        var zipPath = Environment.GetEnvironmentVariable("HIREBOT_E2E_FINAL_ZIP");
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            return;
        }

        FinalPackageTestCasesZipVerifier.AssertAcceptance(zipPath);
    }

    [Fact]
    public void ArtifactStorePackages_ShouldContainTestcasesInIntermediateAndFinal_WhenEnvSessionProvided()
    {
        var sessionId = Environment.GetEnvironmentVariable("HIREBOT_E2E_SESSION_ID");
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        FinalPackageTestCasesZipVerifier.AssertArtifactStorePackages(sessionId);
    }

    [Fact]
    public void FinalPackageZipVerifier_WithSyntheticValidZip_ShouldPass()
    {
        var zipPath = CreateSyntheticAcceptanceZip();
        try
        {
            FinalPackageTestCasesZipVerifier.AssertAcceptance(zipPath);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
    }

    [Fact]
    public void FinalPackageZipVerifier_WhenMissingTestcases_ShouldFail()
    {
        var zipPath = CreateZipWithoutTestcases();
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() =>
                FinalPackageTestCasesZipVerifier.AssertAcceptance(zipPath));
            Assert.Contains("MISSING", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
    }

    private static string CreateSyntheticAcceptanceZip()
    {
        var merged = PackagingTestCasesJsonValidator.AppendPackagingMetadata(
            """
            {
              "description": "雇佣评估（合并）",
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
            """.Trim(),
            "packaging-merged");

        var historyDerived = """
            {
              "description": "历史",
              "role": "digital_employee",
              "industry": "general",
              "source": "history-derived",
              "test_cases": []
            }
            """;

        var materialsDerived = """
            {
              "description": "资料",
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
                  "expected_output": {
                    "resolution": "完成",
                    "user_satisfaction": "满意",
                    "artifacts_created": []
                  }
                }
              ]
            }
            """;

        var templateDerived = """
            {
              "description": "模板",
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
                  "expected_output": {
                    "resolution": "完成",
                    "user_satisfaction": "满意",
                    "artifacts_created": []
                  }
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

        return CreateZip(
            (FinalPackageTestCasesZipVerifier.MergedPath, merged),
            (FinalPackageTestCasesZipVerifier.SourcesIndexPath, indexJson),
            (FinalPackageTestCasesZipVerifier.HistoryDerivedPath, historyDerived),
            (FinalPackageTestCasesZipVerifier.MaterialsDerivedPath, materialsDerived),
            (FinalPackageTestCasesZipVerifier.TemplateDerivedPath, templateDerived),
            ("manifest.json", """{"name":"synthetic-final-acceptance"}"""));
    }

    private static string CreateZipWithoutTestcases()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"hirebot-final-acceptance-{Guid.NewGuid():N}.zip");
        var padding = new byte[2048];
        Random.Shared.NextBytes(padding);

        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteZipTextEntry(archive, "manifest.json", """{"name":"no-testcases"}""");
            WriteZipTextEntry(archive, "skills/README.md", "# skills without testcases");
            var entry = archive.CreateEntry("padding/large.bin", CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            entryStream.Write(padding, 0, padding.Length);
        }

        return zipPath;
    }

    private static string CreateZip(params (string Path, string Content)[] entries)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"hirebot-final-acceptance-{Guid.NewGuid():N}.zip");
        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (path, content) in entries)
            {
                WriteZipTextEntry(archive, path, content);
            }
        }

        return zipPath;
    }

    private static void WriteZipTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        var bytes = Encoding.UTF8.GetBytes(content);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }
}
