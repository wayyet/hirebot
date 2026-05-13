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
    private string? ResolvePhysicalAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalizedRelative = relativePath
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');
        if (normalizedRelative.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedRelative = normalizedRelative["resources/".Length..];
        }

        var candidate = Path.GetFullPath(Path.Combine(
            evaluationResourceRoot,
            normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(evaluationResourceRoot));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidate;
    }

    private static EvaluationReadinessDto BuildReadiness(bool testcaseReady, bool ontologyReady)
    {
        if (testcaseReady && ontologyReady)
        {
            return new EvaluationReadinessDto(
                TestcasesReady: true,
                OntologyReady: true,
                Status: "ready",
                Message: "Testcases and ontology are ready");
        }

        string message;
        string? recommendedAction;

        if (!testcaseReady && !ontologyReady)
        {
            message = "No test cases or ontology files found. Place testcase JSON files under 'testcases/' and ontology files under 'ontology/' in the target hire artifact package.";
            recommendedAction = "Upload testcase JSON files (containing 'test_case' fields and expected steps) under 'testcases/' directory, and ontology .md/.txt/.json files (defining scoring dimensions and rules) under 'ontology/' directory in the target sandbox artifact package, then rerun LOAD_SKILL or START.";
        }
        else if (!testcaseReady)
        {
            message = "No test cases found. Place testcase JSON files under 'testcases/' in the target hire artifact package.";
            recommendedAction = "Upload testcase JSON files (with 'test_case' identifiers and step definitions) under 'testcases/' in the target sandbox artifact package, then rerun LOAD_SKILL or START.";
        }
        else
        {
            message = "No ontology found. Place ontology files under 'ontology/' in the target hire artifact package.";
            recommendedAction = "Upload ontology .md, .txt, or .json files (defining evaluation dimensions, weights, and scoring rules) under 'ontology/' in the target sandbox artifact package, then rerun LOAD_SKILL or START.";
        }

        return new EvaluationReadinessDto(
            TestcasesReady: testcaseReady,
            OntologyReady: ontologyReady,
            Status: "waiting_materials",
            Message: message,
            RecommendedAction: recommendedAction);
    }

    private static string NormalizeAssetType(string assetType)
    {
        return string.IsNullOrWhiteSpace(assetType)
            ? "asset"
            : assetType.Trim().ToLowerInvariant();
    }

    private static EvaluationAssetRefDto ToAssetRef(EvaluationAssetEntity assetEntity)
    {
        return new EvaluationAssetRefDto(
            AssetType: assetEntity.AssetType,
            RelatedKey: assetEntity.RelatedKey ?? string.Empty,
            RelativePath: assetEntity.RelativePath,
            PublicUrl: assetEntity.PublicUrl,
            CreatedAtUtc: assetEntity.CreatedAtUtc.ToString("o"));
    }

    private static string BuildEvaluationSessionId()
    {
        return $"eval_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
    }

    private static string ResolveEvaluationResourceRoot(string contentRootPath, string? configuredResourceRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredResourceRoot))
        {
            return Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot", "resources"));
        }

        return Path.IsPathRooted(configuredResourceRoot)
            ? Path.GetFullPath(configuredResourceRoot.Trim())
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredResourceRoot.Trim()));
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string? ResolveFixtureRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "InstanceFixtures"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "HireBot.ApiService", "Assets", "InstanceFixtures"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "InstanceFixtures")),
            Path.Combine(AppContext.BaseDirectory, "Assets", "InstanceFixtures")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? ResolveScopedFixtureTestcaseRoot(string fixtureRoot, string targetHireId)
    {
        if (string.IsNullOrWhiteSpace(fixtureRoot) || string.IsNullOrWhiteSpace(targetHireId))
        {
            return null;
        }

        var normalizedHireId = targetHireId.Trim();
        var candidates = new[]
        {
            Path.Combine(fixtureRoot, normalizedHireId, "testcases"),
            Path.Combine(fixtureRoot, normalizedHireId.Replace("hire_", "e_", StringComparison.OrdinalIgnoreCase), "testcases")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? ResolveScopedFixtureOntologyRoot(string fixtureRoot, string targetHireId)
    {
        if (string.IsNullOrWhiteSpace(fixtureRoot) || string.IsNullOrWhiteSpace(targetHireId))
        {
            return null;
        }

        var normalizedHireId = targetHireId.Trim();
        var candidates = new[]
        {
            Path.Combine(fixtureRoot, normalizedHireId, "ontology"),
            Path.Combine(fixtureRoot, normalizedHireId.Replace("hire_", "e_", StringComparison.OrdinalIgnoreCase), "ontology")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static IReadOnlyList<string> BuildTemplateHints(EmployeeDetailDto employee)
    {
        var hints = new List<string>();
        void AddHint(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var normalized = raw.Trim();
            if (hints.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            hints.Add(normalized);
        }

        AddHint(employee.SourceTemplateId);
        AddHint(employee.BasedOnTemplateId);
        AddHint(employee.RoleName);

        var binding = ResolveFixtureTemplateBinding(employee.SourceTemplateId ?? employee.BasedOnTemplateId);
        AddHint(binding?.FixtureTemplateId);
        if (!string.IsNullOrWhiteSpace(binding?.FixtureEmployeeId))
        {
            var fixtureEmployeeId = binding.FixtureEmployeeId!.Trim();
            AddHint(fixtureEmployeeId);
            if (fixtureEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
            {
                AddHint($"hire_{fixtureEmployeeId[2..]}");
            }
        }

        return hints;
    }

    private static IReadOnlyList<string> ResolveTemplateScopedFixtureTestcaseRoots(
        string fixtureRoot,
        IReadOnlyList<string> templateHints)
    {
        if (string.IsNullOrWhiteSpace(fixtureRoot) ||
            !Directory.Exists(fixtureRoot) ||
            templateHints.Count == 0)
        {
            return [];
        }

        var resolvedRoots = new List<string>();
        var normalizedHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in templateHints.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            normalizedHints.Add(hint.Trim());
            var binding = ResolveFixtureTemplateBinding(hint);
            if (!string.IsNullOrWhiteSpace(binding?.FixtureTemplateId))
            {
                normalizedHints.Add(binding.FixtureTemplateId!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(binding?.FixtureEmployeeId))
            {
                var fixtureEmployeeId = binding.FixtureEmployeeId!.Trim();
                var byEmployee = Path.Combine(fixtureRoot, fixtureEmployeeId, "testcases");
                if (Directory.Exists(byEmployee))
                {
                    resolvedRoots.Add(byEmployee);
                }

                if (fixtureEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
                {
                    var byHire = Path.Combine(fixtureRoot, $"hire_{fixtureEmployeeId[2..]}", "testcases");
                    if (Directory.Exists(byHire))
                    {
                        resolvedRoots.Add(byHire);
                    }
                }
            }
        }

        foreach (var fixtureDirectory in Directory.GetDirectories(fixtureRoot))
        {
            var instancePath = Path.Combine(fixtureDirectory, "instance.json");
            if (!File.Exists(instancePath))
            {
                continue;
            }

            string? instanceTemplateId = null;
            string? instanceEmployeeId = null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(instancePath));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    instanceTemplateId = TryGetString(document.RootElement, "templateId");
                    instanceEmployeeId = TryGetString(document.RootElement, "employeeId");
                }
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(instanceTemplateId) &&
                string.IsNullOrWhiteSpace(instanceEmployeeId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(instanceTemplateId) &&
                !normalizedHints.Contains(instanceTemplateId.Trim()))
            {
                var normalizedEmployeeId = instanceEmployeeId?.Trim();
                var matchedByEmployeeId = !string.IsNullOrWhiteSpace(normalizedEmployeeId) &&
                                          normalizedHints.Contains(normalizedEmployeeId);
                var matchedByHireId = !string.IsNullOrWhiteSpace(normalizedEmployeeId) &&
                                      normalizedHints.Contains(
                                          normalizedEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase)
                                              ? $"hire_{normalizedEmployeeId[2..]}"
                                              : normalizedEmployeeId);
                if (!matchedByEmployeeId && !matchedByHireId)
                {
                    continue;
                }
            }

            var testcaseRoot = Path.Combine(fixtureDirectory, "testcases");
            if (Directory.Exists(testcaseRoot))
            {
                resolvedRoots.Add(testcaseRoot);
            }
        }

        return resolvedRoots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveTemplateScopedFixtureOntologyRoots(
        string fixtureRoot,
        IReadOnlyList<string> templateHints)
    {
        var testcaseRoots = ResolveTemplateScopedFixtureTestcaseRoots(fixtureRoot, templateHints);
        if (testcaseRoots.Count == 0)
        {
            return [];
        }

        var ontologyRoots = new List<string>();
        foreach (var testcaseRoot in testcaseRoots)
        {
            var fixtureDirectory = Directory.GetParent(testcaseRoot)?.FullName;
            if (string.IsNullOrWhiteSpace(fixtureDirectory))
            {
                continue;
            }

            var ontologyRoot = Path.Combine(fixtureDirectory, "ontology");
            if (Directory.Exists(ontologyRoot))
            {
                ontologyRoots.Add(ontologyRoot);
            }
        }

        return ontologyRoots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, FixtureTemplateBinding> LoadFixtureTemplateBindings()
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        var bindingPath = Path.Combine(fixtureRoot, "template-bindings.json");
        if (!File.Exists(bindingPath))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(bindingPath));
            var root = doc.RootElement;
            var items = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("bindings", out var bindings) &&
                     bindings.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(bindings.EnumerateArray());
            }

            var map = new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var templateId = TryGetString(item, "templateId");
                if (string.IsNullOrWhiteSpace(templateId))
                {
                    continue;
                }

                var fixtureTemplateId = TryGetString(item, "fixtureTemplateId");
                var fixtureEmployeeId = TryGetString(item, "fixtureEmployeeId");
                map[templateId.Trim()] = new FixtureTemplateBinding(
                    TemplateId: templateId.Trim(),
                    FixtureTemplateId: string.IsNullOrWhiteSpace(fixtureTemplateId) ? null : fixtureTemplateId.Trim(),
                    FixtureEmployeeId: string.IsNullOrWhiteSpace(fixtureEmployeeId) ? null : fixtureEmployeeId.Trim());
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static FixtureTemplateBinding? ResolveFixtureTemplateBinding(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return FixtureTemplateBindings.Value.TryGetValue(templateId.Trim(), out var binding)
            ? binding
            : null;
    }

    private static string TryGetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? fallback,
            JsonValueKind.Number => property.GetRawText(),
            _ => fallback
        };
    }

    private static string? TryReadUserRequestFromRawTestcase(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("input", out var inputElement) &&
                inputElement.ValueKind == JsonValueKind.Object)
            {
                if (inputElement.TryGetProperty("user_request", out var userRequestElement) &&
                    userRequestElement.ValueKind == JsonValueKind.String)
                {
                    return userRequestElement.GetString()?.Trim();
                }

                if (inputElement.TryGetProperty("prompt", out var promptElement) &&
                    promptElement.ValueKind == JsonValueKind.String)
                {
                    return promptElement.GetString()?.Trim();
                }
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static EvaluationSandboxConversationStateDto BuildSandboxConversationState(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        HiringConversationTimelineDto timeline,
        IReadOnlyList<EvaluationQuestionCardDto>? questionCards = null)
    {
        return new EvaluationSandboxConversationStateDto(
            EmployeeId: employee.EmployeeId,
            EvalPhase: string.IsNullOrWhiteSpace(employee.EvalPhase) ? "pending_skill_upload" : employee.EvalPhase,
            TargetHireId: workspaceContext.TargetHireId,
            TargetRuntimeId: workspaceContext.TargetHireId,
            TargetSandboxId: workspaceContext.TargetSandboxId,
            EvaluatorHireId: workspaceContext.EvaluatorHireId,
            EvaluatorRuntimeId: workspaceContext.EvaluatorHireId,
            EvaluatorSandboxId: workspaceContext.EvaluatorSandboxId,
            SessionId: timeline.SessionId,
            SkillLoadedAtUtc: workspaceContext.SkillLoadedAtUtc,
            Messages: timeline.Messages,
            QuestionCards: questionCards);
    }

    private static IReadOnlyList<EmployeeCapabilityDto> MergeEvaluationCapabilities(
        IReadOnlyList<EmployeeCapabilityDto> existingCapabilities,
        IReadOnlyList<string> evaluationSkills)
    {
        var merged = existingCapabilities.ToList();
        foreach (var skill in evaluationSkills)
        {
            var index = merged.FindIndex(item => item.Name.Equals(skill, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                merged[index] = merged[index] with { Ready = true };
                continue;
            }

            merged.Add(new EmployeeCapabilityDto(skill, true));
        }

        return merged;
    }

    private static EmployeeDetailDto BuildAiPassResult(EmployeeDetailDto employee, string? sandboxSummary = null)
    {
        var isPrivateBranch = string.Equals(employee.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase);
        return employee with
        {
            // 私有分支是已上岗个人分身的原地定制版本，AI 评估通过后不进入 interning_human，
            // 只通过 EvalPhase 标记“等待用户自评”。普通/雇佣员工仍走原来的 interning_human。
            Status = isPrivateBranch ? "live" : "interning_human",
            LifecycleStatus = isPrivateBranch ? employee.LifecycleStatus : "pending human review",
            EvalPhase = "pending_human_review",
            StageSummary = string.IsNullOrWhiteSpace(sandboxSummary)
                ? "AI evaluation passed, waiting for human review"
                : $"AI evaluation passed: {sandboxSummary}",
            PrimarySignal = "Pending action: submit human review verdict",
            SignalLevel = "warn",
            PendingActions = ["Submit human review verdict"]
        };
    }

    private static EmployeeDetailDto BuildAiFailResult(EmployeeDetailDto employee, string? sandboxSummary = null)
    {
        var isPrivateBranch = string.Equals(employee.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase);
        return employee with
        {
            // 私有分支评估失败也不能把实例状态改成 failed，否则会中断已上岗分身的对话/IM 使用。
            // 失败信息通过 EvalPhase=pending_review 和提示文案表达；普通/雇佣员工仍按原流程 failed。
            Status = isPrivateBranch ? "live" : "failed",
            LifecycleStatus = isPrivateBranch ? employee.LifecycleStatus : "evaluation failed",
            EvalPhase = "pending_review",
            StageSummary = string.IsNullOrWhiteSpace(sandboxSummary)
                ? "AI evaluation failed, go to Review for rollback or continue hire"
                : $"AI evaluation failed: {sandboxSummary}",
            PrimarySignal = "Pending action: choose a Review fallback path",
            SignalLevel = "error",
            PendingActions = ["Go to Review and choose rollback option"]
        };
    }


    private static string? ExtractTargetRuntimeIdFromComment(string? comment)
    {
        var explicitRuntimeId = FirstNonEmpty(
            ExtractValueFromComment(comment, "targetRuntimeId"),
            ExtractValueFromComment(comment, "targetHireId"),
            ExtractValueFromComment(comment, "hireId"));
        if (!string.IsNullOrWhiteSpace(explicitRuntimeId))
        {
            return explicitRuntimeId;
        }
        return null;
    }

    private static string ResolveTargetTemplateId(EmployeeDetailDto employee)
    {
        var directTemplateId = FirstNonEmpty(employee.SourceTemplateId, employee.BasedOnTemplateId, employee.RoleName);
        if (string.IsNullOrWhiteSpace(directTemplateId))
        {
            return "default";
        }

        var binding = ResolveFixtureTemplateBinding(directTemplateId);
        if (!string.IsNullOrWhiteSpace(binding?.FixtureTemplateId))
        {
            return binding!.FixtureTemplateId!;
        }

        return directTemplateId;
    }

    private static string? ExtractPathFromComment(string? comment)
    {
        return ExtractValueFromComment(comment, "path");
    }

    private static string BuildWorkspaceKey(string owner, string employeeId)
    {
        return $"{owner.Trim()}::{employeeId.Trim()}";
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return string.Empty;
    }

    private static string? ExtractValueFromComment(string? comment, string key)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var trimmed = comment.Trim();
        if (string.Equals(key, "path", StringComparison.OrdinalIgnoreCase) && Directory.Exists(trimmed))
        {
            return trimmed;
        }

        var marker = $"{key}=";
        var markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var value = trimmed[(markerIndex + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var endIndex = value.IndexOf(';');
        if (endIndex >= 0)
        {
            value = value[..endIndex];
        }

        return value.Trim().Trim('"', '\'');
    }

    private static string? NormalizeStatus(string? status, string? lifecycleStatus)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            return normalized switch
            {
                "hired" => "hired",
                "interning_ai" => "interning_ai",
                "interning_human" => "interning_human",
                "live" => "live",
                "failed" => "failed",
                "retired" => "retired",
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            return null;
        }

        var value = lifecycleStatus.Trim().ToLowerInvariant();
        if (value.Contains("failed") || value.Contains("error"))
        {
            return "failed";
        }

        if (value.Contains("ai"))
        {
            return "interning_ai";
        }

        if (value.Contains("human") || value.Contains("onboarding") || value.Contains("intern"))
        {
            return "interning_human";
        }

        if (value.Contains("live"))
        {
            return "live";
        }

        if (value.Contains("retired"))
        {
            return "retired";
        }

        return null;
    }

    private sealed record EvaluationWorkspaceContext(
        string TargetHireId,
        string TargetSandboxId,
        string EvaluatorHireId,
        string EvaluatorSandboxId,
        DateTimeOffset? SkillLoadedAtUtc,
        string? SessionId);

    private sealed record TargetArtifactWarmupResult(
        string WorkspacePath,
        string SourceArtifactPath);

    private sealed record TargetArtifactBundle(
        string FileName,
        byte[] Content,
        string Sha256,
        string SourceType,
        string SourcePath);

    private sealed record EvaluatorVerdictResult(
        bool Passed,
        string Summary,
        decimal OverallScore,
        IReadOnlyList<EvaluationDimensionScoreDto> DimensionScores,
        string RawVerdictJson);

    private sealed record TestcaseSourceFile(
        string FileName,
        string SourcePath,
        string RawJson,
        string SourceType);

    private sealed record TraceExecutionEvidence(
        string TestcaseId,
        string ScenarioName,
        string Input,
        string ExecutionId,
        string TraceJson,
        string TraceAssetUrl);

    private sealed record OntologySourceFile(
        string FileName,
        string SourcePath,
        string Content,
        string SourceType);

    private sealed record OntologyRuleCandidate(
        string Dimension,
        string Text,
        string SourceFile);

    private sealed record OntologyProfile(
        IReadOnlyDictionary<string, decimal> DimensionWeights,
        IReadOnlyList<string> DimensionRules,
        IReadOnlyList<OntologySourceFile> Sources,
        string SourceSummary);

    private sealed record FixtureTemplateBinding(
        string TemplateId,
        string? FixtureTemplateId,
        string? FixtureEmployeeId);

    private sealed record ParsedTestcase(
        string TestcaseId,
        string ScenarioName,
        string SourceFile,
        string SourcePath,
        string RawJson,
        IReadOnlyList<string> ExpectedSteps,
        string InputPrompt);
}

