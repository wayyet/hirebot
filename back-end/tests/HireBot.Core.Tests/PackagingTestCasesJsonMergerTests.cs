using System.Text.Json;
using HireBot.Core.Services.Hiring;

namespace HireBot.Core.Tests;

public class PackagingTestCasesJsonMergerTests
{
    private const string StagedJson = """
        {
          "description": "雇佣评估（合并）",
          "role": "digital_employee",
          "source": "packaging-merged",
          "test_cases": [
            {
              "test_case_id": "TC-001",
              "scenario_name": "咨询业务",
              "input": { "user_request": "请介绍业务流程", "context": {} },
              "expected_behavior_sequence": [],
              "expected_output": { "resolution": "已解答", "user_satisfaction": "满意", "artifacts_created": [] }
            }
          ]
        }
        """;

    [Fact]
    public void TryMergeEvaluationTestCasesJson_WhenExistingIsEmptyFallback_ShouldKeepStagedCases()
    {
        var fallbackJson = """
            {
              "source": "packaging-fallback",
              "test_cases": []
            }
            """;

        var merged = AssertMerge(fallbackJson, StagedJson);

        Assert.Contains("packaging-merged", merged);
        Assert.Contains("TC-001", merged);
        using var doc = JsonDocument.Parse(merged);
        Assert.Equal(1, doc.RootElement.GetProperty("test_cases").GetArrayLength());
    }

    [Fact]
    public void TryMergeEvaluationTestCasesJson_WhenExistingHasSkillGuidedCases_ShouldMergeBoth()
    {
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

        var merged = AssertMerge(skillGuidedJson, StagedJson);
        using var doc = JsonDocument.Parse(merged);
        var cases = doc.RootElement.GetProperty("test_cases");
        Assert.Equal(2, cases.GetArrayLength());

        var ids = cases.EnumerateArray()
            .Select(item => item.TryGetProperty("test_case_id", out var id)
                ? id.GetString()
                : item.TryGetProperty("caseId", out var legacyId) ? legacyId.GetString() : null)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        Assert.Contains("TC-001", ids);
        Assert.Contains("eval-case-001", ids);
        Assert.True(doc.RootElement.TryGetProperty("merged_sources", out var sources));
        Assert.Equal(2, sources.GetArrayLength());
    }

    [Fact]
    public void TryMergeEvaluationTestCasesJson_WhenDuplicateId_ShouldPreferStaged()
    {
        var existingJson = """
            {
              "source": "conversation-skill-guided",
              "test_cases": [
                {
                  "test_case_id": "TC-001",
                  "scenario_name": "旧标题",
                  "input": { "user_request": "旧请求", "context": {} },
                  "expected_behavior_sequence": [],
                  "expected_output": { "resolution": "旧", "user_satisfaction": "一般", "artifacts_created": [] }
                }
              ]
            }
            """;

        var merged = AssertMerge(existingJson, StagedJson);
        using var doc = JsonDocument.Parse(merged);
        var first = doc.RootElement.GetProperty("test_cases")[0];
        Assert.Equal("咨询业务", first.GetProperty("scenario_name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("test_cases").GetArrayLength());
    }

    [Fact]
    public void TryMergeEvaluationTestCasesJson_WhenExistingNull_ShouldReturnStaged()
    {
        Assert.True(PackagingTestCasesJsonMerger.TryMergeEvaluationTestCasesJson(null, StagedJson, out var merged));
        Assert.Equal(StagedJson.Trim(), merged.Trim());
    }

    [Fact]
    public void TryMergeEvaluationTestCasesJson_ShouldEmitPlainChineseNotUnicodeEscapes()
    {
        var existingJson = """
            {
              "source": "conversation-skill-guided",
              "test_cases": [
                {
                  "test_case_id": "eval-case-001",
                  "scenario_name": "访客预约正常流程",
                  "input": { "user_request": "我想预约访客", "context": {} },
                  "expected_behavior_sequence": [],
                  "expected_output": "完成预约"
                }
              ]
            }
            """;

        var merged = AssertMerge(existingJson, StagedJson);

        Assert.Contains("咨询业务", merged);
        Assert.Contains("访客预约正常流程", merged);
        Assert.DoesNotContain(@"\u8BBF", merged);
        Assert.DoesNotContain(@"\u54A8", merged);
        Assert.Contains('\n', merged);
    }

    private static string AssertMerge(string existingJson, string stagedJson)
    {
        Assert.True(PackagingTestCasesJsonMerger.TryMergeEvaluationTestCasesJson(existingJson, stagedJson, out var merged));
        Assert.False(string.IsNullOrWhiteSpace(merged));
        return merged;
    }
}
