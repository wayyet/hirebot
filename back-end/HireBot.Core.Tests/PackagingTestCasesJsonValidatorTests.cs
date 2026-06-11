using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring;

namespace HireBot.Core.Tests;

public class PackagingTestCasesJsonValidatorTests
{
    [Fact]
    public void PrepareHistoryTranscript_ShouldFilterPackagingIntentAndShortUserMessages()
    {
        var messages = new[]
        {
            new HiringConversationMessageDto("1", "user", "hi", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("2", "user", "请开始生成产物包", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("3", "user", "三个阶段均已确认完成，请开始生成数字员工", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("4", "user", "我需要查询订单物流状态", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("5", "assistant", "好的，我来帮您查询物流。", DateTimeOffset.UtcNow)
        };

        var transcript = PackagingTestCasesJsonValidator.PrepareHistoryTranscript(messages);

        Assert.Equal(2, transcript.Count);
        Assert.Equal("user", transcript[0].Role);
        Assert.Contains("物流", transcript[0].Content);
        Assert.Equal("assistant", transcript[1].Role);
    }

    [Theory]
    [InlineData("三个阶段均已确认完成，请开始生成数字员工")]
    [InlineData("已完成全部确认，请生成数字员工")]
    [InlineData("请生成数字员工包")]
    [InlineData("All three stages confirmed. Please generate the digital employee.")]
    [InlineData("Please generate the digital employee package.")]
    [InlineData("All three stages are confirmed. Please generate the instance package as a ZIP.")]
    public void PackagingIntentSupport_WhenDigitalEmployeeGenerationWording_ShouldReturnTrue(string content)
    {
        Assert.True(PackagingIntentSupport.IsPackagingIntent(content));
    }

    [Theory]
    [InlineData("这个数字员工需要支持排产")]
    [InlineData("数字员工角色叫化妆品排产员")]
    public void PackagingIntentSupport_WhenOnlyDescribingDigitalEmployee_ShouldReturnFalse(string content)
    {
        Assert.False(PackagingIntentSupport.IsPackagingIntent(content));
    }

    [Fact]
    public void TryValidateTestCasesJson_WhenValidDemoStructure_ShouldReturnTrue()
    {
        var json = """
            {
              "description": "demo",
              "role": "customer_service",
              "industry": "ecommerce",
              "test_cases": [
                {
                  "test_case_id": "TC-001",
                  "scenario_name": "退货",
                  "input": { "user_request": "我要退货", "context": {} },
                  "expected_behavior_sequence": [
                    { "step": 1, "action": "安抚", "criteria": "友好" }
                  ],
                  "expected_output": {
                    "resolution": "已受理",
                    "user_satisfaction": "满意",
                    "artifacts_created": []
                  }
                }
              ]
            }
            """;

        var valid = PackagingTestCasesJsonValidator.TryValidateTestCasesJson(json, out var normalized);

        Assert.True(valid);
        Assert.Contains("test_cases", normalized);
    }

    [Fact]
    public void TryValidateTestCasesJson_WhenMissingTestCases_ShouldReturnFalse()
    {
        var json = """{ "description": "demo", "role": "x", "industry": "y" }""";

        var valid = PackagingTestCasesJsonValidator.TryValidateTestCasesJson(json, out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryValidateTestCasesJson_WhenUnicodeEscaped_ShouldEmitPlainChineseAndIndentation()
    {
        var json = """
            {
              "schema_version": "1.0",
              "source": "packaging-merged",
              "test_cases": [
                {
                  "test_case_id": "history_visitor_001",
                  "scenario_name": "\u8BBF\u5BA2\u63D0\u4EA4\u9884\u7EA6",
                  "input": { "user_request": "\u6211\u60F3\u63D0\u4EA4\u8BBF\u5BA2\u9884\u7EA6" },
                  "expected_behavior_sequence": ["\u8BC6\u522B\u610F\u56FE"],
                  "expected_output": "\u8FD4\u56DE\u6821\u9A8C\u7ED3\u679C"
                }
              ]
            }
            """;

        var valid = PackagingTestCasesJsonValidator.TryValidateTestCasesJson(json, out var normalized);

        Assert.True(valid);
        Assert.DoesNotContain(@"\u8BBF", normalized, StringComparison.Ordinal);
        Assert.Contains("访客提交预约", normalized, StringComparison.Ordinal);
        Assert.Contains("\n  \"test_cases\"", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateDerivedTestCasesJson_ShouldNotReserializeToHumanReadable()
    {
        var json = """
            {"source":"history-derived","test_cases":[{"test_case_id":"t1","scenario_name":"\u8BBF\u5BA2","input":{"user_request":"\u95EE"}}]}
            """;

        var valid = PackagingTestCasesJsonValidator.TryValidateDerivedTestCasesJson(json, out var normalized);

        Assert.True(valid);
        Assert.Contains(@"\u8BBF", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendPackagingMetadata_ShouldAddSourceAndGeneratedAt()
    {
        var json = """{ "description": "d", "role": "r", "industry": "i", "test_cases": [] }""";

        var enriched = PackagingTestCasesJsonValidator.AppendPackagingMetadata(json, "kingcrab-history-llm");

        using var document = JsonDocument.Parse(enriched);
        var root = document.RootElement;
        Assert.Equal("kingcrab-history-llm", root.GetProperty("source").GetString());
        Assert.True(root.TryGetProperty("generated_at", out _));
    }

    [Fact]
    public void TryExtractEvaluationTestCasesJson_FromTechnicalArtifact_ShouldSucceed()
    {
        var innerJson = """
            {
              "description": "d",
              "role": "r",
              "industry": "i",
              "test_cases": [
                {
                  "test_case_id": "TC-001",
                  "scenario_name": "s",
                  "input": { "user_request": "q", "context": {} },
                  "expected_behavior_sequence": [
                    { "step": 1, "action": "a", "criteria": "c" }
                  ],
                  "expected_output": { "resolution": "ok", "user_satisfaction": "ok", "artifacts_created": [] }
                }
              ]
            }
            """;
        using var artifactDoc = JsonDocument.Parse(
            $$"""{"source":"kingcrab-history-llm","evaluation_test_cases_json":{{JsonSerializer.Serialize(innerJson)}}}""");
        var callback = new HiringDispatchCallbackPayload(
            "packaging-test-cases",
            [],
            "ok",
            [],
            [],
            "success",
            [],
            artifactDoc.RootElement.Clone());

        var extracted = PackagingTestCasesJsonValidator.TryExtractEvaluationTestCasesJson(
            callback,
            out var testCasesJson,
            out var source);

        Assert.True(extracted);
        Assert.Contains("TC-001", testCasesJson);
        Assert.Equal("kingcrab-history-llm", source);
    }

    [Fact]
    public void TryExtractPackagingTestCasesBundle_FromExtendedArtifact_ShouldReturnAllParts()
    {
        var merged = BuildDerivedPayload("packaging-merged", "TC-001");
        var history = BuildDerivedPayload("history-derived", "TC-H01", emptyCases: true);
        var materials = BuildDerivedPayload("materials-derived", "TC-M01");
        var template = BuildDerivedPayload("template-derived", "TC-T01");
        var index = """
            {
              "generated_at": "2026-05-28T12:00:00Z",
              "primary": "testcases/evaluation-test-cases.json",
              "sources": {
                "history": "ontology/hiring-session/testcases-sources/history-derived.json",
                "materials": "ontology/hiring-session/testcases-sources/materials-derived.json",
                "template": "ontology/hiring-session/testcases-sources/template-derived.json"
              },
              "inputs_summary": { "history_turns": 1, "material_files": 1, "template_files": 1 }
            }
            """;

        using var artifactDoc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            source = "packaging-merged",
            evaluation_test_cases_json = merged,
            testcases_sources_index_json = index,
            history_derived_json = history,
            materials_derived_json = materials,
            template_derived_json = template
        }));

        var callback = new HiringDispatchCallbackPayload(
            "packaging-test-cases",
            [],
            "ok",
            [],
            [],
            "success",
            [],
            artifactDoc.RootElement.Clone());

        var extracted = PackagingTestCasesJsonValidator.TryExtractPackagingTestCasesBundle(callback, out var bundle);

        Assert.True(extracted);
        Assert.Contains("TC-001", bundle.MergedJson);
        Assert.Contains("history-derived.json", bundle.SourcesIndexJson);
        Assert.Contains("TC-M01", bundle.MaterialsDerivedJson);
        Assert.Equal("packaging-merged", bundle.Source);
    }

    [Fact]
    public void TryValidateDerivedTestCasesJson_WhenEmptyCases_ShouldReturnTrue()
    {
        var json = """{"description":"d","role":"r","industry":"i","source":"history-derived","test_cases":[]}""";

        var valid = PackagingTestCasesJsonValidator.TryValidateDerivedTestCasesJson(json, out var normalized);

        Assert.True(valid);
        Assert.Contains("history-derived", normalized);
    }

    [Fact]
    public void TryValidateSourcesIndexJson_WhenLegacyArraySources_ShouldReturnTrue()
    {
        var json = """
            {
              "schema_version": "1.0",
              "generated_at": "2026-05-28T07:59:00Z",
              "sources": [
                { "source": "materials-derived", "path": "ontology/hiring-session/testcases-sources/materials-derived.json", "count": 3 }
              ]
            }
            """;

        var valid = PackagingTestCasesJsonValidator.TryValidateSourcesIndexJson(json, out _);

        Assert.True(valid);
        using var document = JsonDocument.Parse(json);
        Assert.True(PackagingTestCasesJsonValidator.TryGetSourcesIndexMaterialFileCount(document.RootElement, out var count));
        Assert.Equal(3, count);
    }

    private static string BuildDerivedPayload(string source, string testCaseId, bool emptyCases = false)
    {
        if (emptyCases)
        {
            return $$"""{"description":"d","role":"r","industry":"i","source":"{{source}}","test_cases":[]}""";
        }

        return $$"""
            {
              "description": "d",
              "role": "r",
              "industry": "i",
              "source": "{{source}}",
              "test_cases": [
                {
                  "test_case_id": "{{testCaseId}}",
                  "scenario_name": "s",
                  "input": { "user_request": "q", "context": {} },
                  "expected_behavior_sequence": [
                    { "step": 1, "action": "a", "criteria": "c" },
                    { "step": 2, "action": "b", "criteria": "d" }
                  ],
                  "expected_output": { "resolution": "ok", "user_satisfaction": "ok", "artifacts_created": [] }
                }
              ]
            }
            """;
    }
}
