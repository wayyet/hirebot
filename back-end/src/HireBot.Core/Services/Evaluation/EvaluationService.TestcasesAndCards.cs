using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

internal sealed partial class EvaluationService
{

    private static IReadOnlyList<ParsedTestcase> ParseTestcases(
        string sourceFile,
        string sourcePath,
        string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            var caseElements = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("test_cases", out var testCasesElement) &&
                testCasesElement.ValueKind == JsonValueKind.Array)
            {
                caseElements.AddRange(testCasesElement.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                caseElements.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("test_case_id", out _))
            {
                caseElements.Add(root);
            }
            else
            {
                return [];
            }

            var parsed = new List<ParsedTestcase>();
            for (var index = 0; index < caseElements.Count; index++)
            {
                var caseElement = caseElements[index];
                var testcaseId = TryGetString(caseElement, "test_case_id", $"{Path.GetFileNameWithoutExtension(sourceFile)}-{index + 1:D2}");
                var scenarioName = TryGetString(caseElement, "scenario_name", testcaseId);
                var expectedSteps = ParseExpectedSteps(caseElement);
                var rawCase = caseElement.GetRawText();
                var inputPrompt = TryReadUserRequestFromRawTestcase(rawCase) ?? scenarioName;

                parsed.Add(new ParsedTestcase(
                    TestcaseId: testcaseId,
                    ScenarioName: scenarioName,
                    SourceFile: sourceFile,
                    SourcePath: sourcePath,
                    RawJson: rawCase,
                    ExpectedSteps: expectedSteps,
                    InputPrompt: inputPrompt));
            }

            return parsed;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseExpectedSteps(JsonElement testcaseElement)
    {
        if (!testcaseElement.TryGetProperty("expected_behavior_sequence", out var stepsElement) ||
            stepsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var steps = new List<string>();
        foreach (var step in stepsElement.EnumerateArray())
        {
            var action = TryGetString(step, "action", string.Empty);
            var criteria = TryGetString(step, "criteria", string.Empty);
            var order = TryGetString(step, "step", string.Empty);
            if (string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(criteria))
            {
                continue;
            }

            var rendered = string.IsNullOrWhiteSpace(order)
                ? $"{action} | {criteria}".Trim(' ', '|')
                : $"{order}. {action} | {criteria}".Trim(' ', '|');
            steps.Add(rendered);
        }

        return steps;
    }

    private static IReadOnlyList<EvaluationQuestionCardDto> BuildQuestionCards(IReadOnlyList<ParsedTestcase> parsedTestcases)
    {
        return parsedTestcases
            .GroupBy(
                testcase => string.IsNullOrWhiteSpace(testcase.TestcaseId)
                    ? testcase.ScenarioName
                    : testcase.TestcaseId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
            .Select(testcase => new EvaluationQuestionCardDto(
                TestcaseId: testcase.TestcaseId,
                Title: testcase.ScenarioName,
                Prompt: testcase.InputPrompt,
                ScoringHint: "Score by ontology dimensions and expected behavior alignment.",
                Steps: testcase.ExpectedSteps,
                SourceFile: testcase.SourceFile))
            .ToArray();
    }

    private async Task<IReadOnlyList<EvaluationQuestionCardDto>> BuildQuestionCardsFromAssetsAsync(
        IReadOnlyList<EvaluationAssetEntity> testcaseAssets,
        CancellationToken cancellationToken)
    {
        var parsed = new List<ParsedTestcase>();
        foreach (var testcaseAsset in testcaseAssets)
        {
            var physicalPath = ResolvePhysicalAssetPath(testcaseAsset.RelativePath);
            if (string.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath))
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(physicalPath, cancellationToken);
            parsed.AddRange(ParseTestcases(Path.GetFileName(physicalPath), testcaseAsset.RelativePath, json));
        }

        return BuildQuestionCards(parsed);
    }

    private async Task<IReadOnlyList<EvaluationQuestionCardDto>?> LoadQuestionCardsForSessionAsync(
        Guid sessionEntityId,
        CancellationToken cancellationToken)
    {
        var allAssets = await dbContext.EvaluationAssets
            .AsNoTracking()
            .Where(item =>
                item.SessionEntityId == sessionEntityId &&
                item.AssetType == "testcases-json")
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (allAssets.Count == 0)
        {
            return null;
        }

        var deduplicated = allAssets
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.RelatedKey)
                    ? item.RelativePath
                    : item.RelatedKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(5)
            .ToArray();

        var cards = await BuildQuestionCardsFromAssetsAsync(deduplicated, cancellationToken);
        return cards.Count > 0 ? cards : null;
    }

    private async Task<IReadOnlyList<EvaluationQuestionCardDto>?> LoadQuestionCardsForLatestSessionAsync(
        string scope,
        string employeeId,
        CancellationToken cancellationToken)
    {
        var latestSession = await dbContext.EvaluationSessions
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == scope &&
                item.EmployeeId == employeeId.Trim())
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSession is null)
        {
            return null;
        }

        return await LoadQuestionCardsForSessionAsync(latestSession.Id, cancellationToken);
    }

    private static bool HasMaterialsSupplementPrompt(IReadOnlyList<HiringConversationMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            return false;
        }

        return messages.Any(message =>
            string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(message.Content) &&
            message.Content.Contains("评估资料不完整", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasEvaluationReadyPrompt(IReadOnlyList<HiringConversationMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            return false;
        }

        return messages.Any(message =>
            string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(message.Content) &&
            (message.Content.Contains("以下是本轮考题卡片", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("评分标准（按评估本体维度）", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("你可以继续对话询问题卡细节、评分标准", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasTargetSandboxContextPrompt(IReadOnlyList<HiringConversationMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            return false;
        }

        return messages.Any(message =>
            string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(message.Content) &&
            message.Content.Contains("目标沙箱连接上下文已就绪", StringComparison.Ordinal));
    }

    private static string BuildQuestionCardsMarkdown(IReadOnlyList<EvaluationQuestionCardDto> cards)
    {
        if (cards.Count == 0)
        {
            return "当前未解析到可展示的考题卡片，请先确认测试用例是否已成功加载。";
        }

        var lines = new List<string>
        {
            "以下是本轮考题卡片："
        };

        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            lines.Add($"{index + 1}. [{card.TestcaseId}] {card.Title}");
            lines.Add($"   题目：{card.Prompt}");
            if (card.Steps.Count > 0)
            {
                lines.Add($"   关键步骤：{string.Join("；", card.Steps)}");
            }

            if (!string.IsNullOrWhiteSpace(card.ScoringHint))
            {
                lines.Add($"   判分提示：{card.ScoringHint}");
            }
        }

        lines.Add("如需我解释某一题的评分标准，请直接说“解释第 N 题”。");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOntologyRulesMarkdown()
    {
        var lines = new List<string>
        {
            "评分标准（按评估本体维度）："
        };

        foreach (var weight in DefaultOntologyWeights)
        {
            lines.Add($"- {ToDimensionDisplayName(weight.Key)}（权重 {weight.Value:0.##}）");
        }

        foreach (var rule in DefaultOntologyRules)
        {
            lines.Add($"- {rule}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildMissingMaterialsSummary(bool testcaseReady, bool ontologyReady)
    {
        if (!testcaseReady && !ontologyReady)
        {
            return "缺失测试用例与评估本体";
        }

        if (!testcaseReady)
        {
            return "缺失测试用例";
        }

        if (!ontologyReady)
        {
            return "缺失评估本体";
        }

        return "测试用例与评估本体均已就绪";
    }

    private static string NormalizeEvaluationStatus(string? sessionStatus)
    {
        if (string.IsNullOrWhiteSpace(sessionStatus))
        {
            return "pending";
        }

        return sessionStatus.Trim().ToLowerInvariant();
    }

    private static string BuildEvaluationRecommendation(
        EvaluationReportSummaryDto? latestReport,
        EvaluationReadinessDto? readiness,
        string sessionStatus)
    {
        if (latestReport is not null)
        {
            return latestReport.Passed
                ? "Evaluation report passed. Submit human review decision to continue onboarding."
                : "Evaluation report failed. Fix issues from report and rerun evaluation.";
        }

        if (readiness is null)
        {
            return "Evaluation session exists but readiness data is unavailable yet.";
        }

        if (!readiness.TestcasesReady && !readiness.OntologyReady)
        {
            return "Testcases and ontology are not ready in evaluator sandbox materials. Upload both under 'testcases/' and 'ontology/', then rerun LOAD_SKILL or START.";
        }

        if (!readiness.TestcasesReady)
        {
            return "Testcases are not ready in evaluator sandbox materials. Upload testcase JSON files (with 'test_case' fields) under 'testcases/', then rerun LOAD_SKILL or START.";
        }

        if (!readiness.OntologyReady)
        {
            return "Ontology is not ready in evaluator sandbox materials. Upload ontology .md/.txt/.json files (with dimension and rule definitions) under 'ontology/', then rerun LOAD_SKILL or START.";
        }

        return sessionStatus switch
        {
            "ready" => "Testcases and ontology are ready. Confirm question cards and run evaluation.",
            "target_executed" => "Target execution trace captured. Run scoring and persist report.",
            _ => "Evaluation session is active. Continue the next evaluation step."
        };
    }

    private static IReadOnlyList<EvaluationScenarioDto> BuildScenariosFromQuestionCards(
        IReadOnlyList<EvaluationQuestionCardDto> questionCards,
        EvaluationSessionEntity sessionEntity,
        EvaluationReportSummaryDto? latestReport)
    {
        if (questionCards.Count == 0)
        {
            return [];
        }

        var scenarioStatus = latestReport is null
            ? "pending"
            : "completed";
        var verdict = latestReport is null
            ? null
            : latestReport.Passed
                ? "passed"
                : "failed";
        var verdictComment = latestReport is null
            ? null
            : "Verdict derived from latest evaluation report.";
        var startedAtUtc = sessionEntity.CreatedAtUtc.ToString("o");
        var completedAtUtc = latestReport?.CreatedAtUtc;

        return questionCards
            .Select(card => new EvaluationScenarioDto(
                ScenarioId: card.TestcaseId,
                ScenarioName: card.Title,
                Status: scenarioStatus,
                Verdict: verdict,
                VerdictComment: verdictComment,
                MessageCount: 0,
                StartedAt: startedAtUtc,
                CompletedAt: completedAtUtc))
            .ToArray();
    }

    private static EvaluationScenarioDto BuildSummaryScenario(
        EvaluationSessionEntity sessionEntity,
        EvaluationReportSummaryDto latestReport)
    {
        return new EvaluationScenarioDto(
            ScenarioId: $"report_{latestReport.Iteration}",
            ScenarioName: $"评估轮次 #{latestReport.Iteration}",
            Status: "completed",
            Verdict: latestReport.Passed ? "passed" : "failed",
            VerdictComment: "该结果由最新落库评估报告生成。",
            MessageCount: 0,
            StartedAt: sessionEntity.CreatedAtUtc.ToString("o"),
            CompletedAt: latestReport.CreatedAtUtc);
    }

}
