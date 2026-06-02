using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private const string EvaluationConsumerSkillName = "evaluation-expert-consumer";
    private const string EvaluationConsumerSkillWorkspaceRoot = "/workspace/skills/evaluation-expert-consumer";
    private const string EvaluationConsumerMaterialUploadRoot = "uploads/evaluation-expert-consumer";
    private const string EvaluationConsumerTestCasesTargetDir = $"{EvaluationConsumerMaterialUploadRoot}/test-cases";
    private const string EvaluationConsumerOntologyTargetDir = $"{EvaluationConsumerMaterialUploadRoot}/ontology";
    private const string DefaultEvaluationConsumerDriverId = "ws_jwt";
    private const string DefaultEvaluationConsumerSimulatorId = "customer_realistic";

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
            message = "No test cases or ontology files found in evaluator sandbox materials.";
            recommendedAction = "Upload testcase JSON files (containing 'test_case' fields and expected steps) under 'testcases/' and ontology .md/.txt/.json files (defining scoring dimensions and rules) under 'ontology/' in evaluator sandbox materials, then rerun LOAD_SKILL or START.";
        }
        else if (!testcaseReady)
        {
            message = "No test cases found in evaluator sandbox materials.";
            recommendedAction = "Upload testcase JSON files (with 'test_case' identifiers and step definitions) under 'testcases/' in evaluator sandbox materials, then rerun LOAD_SKILL or START.";
        }
        else
        {
            message = "No ontology found in evaluator sandbox materials.";
            recommendedAction = "Upload ontology .md, .txt, or .json files (defining evaluation dimensions, weights, and scoring rules) under 'ontology/' in evaluator sandbox materials, then rerun LOAD_SKILL or START.";
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

    private static string ResolveEvaluationResourceRoot(
        string contentRootPath,
        string? configuredDataRoot,
        string? configuredResourceRoot)
    {
        return HireBotPathResolver.ResolveEvaluationResourceRoot(
            contentRootPath,
            configuredDataRoot,
            configuredResourceRoot);
    }

    private static string ResolveEvaluationTemplatePackageRoot(string contentRootPath, string? configuredTemplatesRoot)
    {
        var templatesRoot = string.IsNullOrWhiteSpace(configuredTemplatesRoot)
            ? Path.GetFullPath(Path.Combine(contentRootPath, "Assets", "DigitalEmployeeTemplates"))
            : Path.IsPathRooted(configuredTemplatesRoot)
                ? Path.GetFullPath(configuredTemplatesRoot.Trim())
                : Path.GetFullPath(Path.Combine(contentRootPath, configuredTemplatesRoot.Trim()));

        return Path.GetFullPath(Path.Combine(templatesRoot, "evaluation-expert"));
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string? ResolveFixtureRoot()
    {
        return HireBotPathResolver.ResolveConventionalInstanceFixturesRoot();
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

    private async Task<EvaluationWorkspaceContext?> LoadWorkspaceContextAsync(
        string owner,
        string employeeId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.EvaluationWorkspaceStates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.OwnerSubject == owner && item.EmployeeId == employeeId,
                cancellationToken);
        if (entity is null || string.IsNullOrWhiteSpace(entity.PayloadJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<EvaluationWorkspaceContext>(entity.PayloadJson, JsonOptions);
    }

    private async Task SaveWorkspaceContextAsync(
        string owner,
        string employeeId,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.EvaluationWorkspaceStates
            .FirstOrDefaultAsync(
                item => item.OwnerSubject == owner && item.EmployeeId == employeeId,
                cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var payloadJson = JsonSerializer.Serialize(workspaceContext, JsonOptions);

        if (entity is null)
        {
            dbContext.EvaluationWorkspaceStates.Add(new EvaluationWorkspaceStateEntity
            {
                OwnerSubject = owner,
                EmployeeId = employeeId,
                PayloadJson = payloadJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            entity.PayloadJson = payloadJson;
            entity.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static int NormalizeConsumerGlobalTurnCap(int configuredValue)
    {
        if (configuredValue <= 0)
        {
            return 30;
        }

        return Math.Clamp(configuredValue, 1, 50);
    }

    private static EvaluationRuntimeContextEmployeeProvenance ResolveEmployeeProvenance(EmployeeDetailDto employee)
    {
        if (!string.IsNullOrWhiteSpace(employee.FromInstanceId))
        {
            return new(Source: "instance_cloned", Reliability: "high",
                Caveat: $"Cloned from instance {employee.FromInstanceId}.");
        }

        if (!string.IsNullOrWhiteSpace(employee.SourceTemplateId))
        {
            return new(Source: "template_resolved", Reliability: "medium",
                Caveat: $"Resolved from template {employee.SourceTemplateId}, no authoritative employee profile on file.");
        }

        return new(Source: "inferred_fallback", Reliability: "low",
            Caveat: "employee_inferred_no_authoritative_source");
    }

    private static string NormalizeConsumerPathSegment(string value, string fallback)
    {
        var sanitized = value.Replace('\\', '/');
        var segments = sanitized.Split('/');
        if (segments.Any(static s => s is ".." or "..."))
        {
            return fallback;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                builder.Append(c);
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
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
        string? SessionId,
        string? EvaluatorTemplatePackageZipPath,
        string? UploadedTemplatePackageZipPath,
        string? ArtifactWorkspaceDir,
        Dictionary<string, WorkspaceStepState> StepStates,
        IReadOnlyList<EvaluationTestcaseOutline>? TestcaseOutlines = null);

    private sealed record TemplatePackageUploadResult(
        string? SandboxTemplatePackageZipPath,
        string? UploadedTemplatePackageZipPath);

    /// <summary>
    /// 从雇佣端加载的模板归档，包含字节内容、文件名及本地缓存路径。
    /// 由 <see cref="EvaluationService.UploadHiringTemplateToTargetSandboxAsync"/> 生成，
    /// 供后续 evaluator workspace 上传复用，避免二次加载。
    /// </summary>
    private sealed record HiringTemplateArchive(
        byte[] ArchiveBytes,
        string FileName,
        string? LocalCachePath,
        IReadOnlyList<TemplateMaterialFile> MaterialFiles);

    /// <summary>
    /// 从模板包中提取的单个材料文件（testcase 或 ontology）。
    /// </summary>
    private sealed record TemplateMaterialFile(
        string TargetDir,
        string FileName,
        byte[] Content);

    /// <summary>
    /// 评估用例大纲条目，供前端展示评估场景列表。
    /// </summary>
    private sealed record EvaluationTestcaseOutline(
        string TestcaseId,
        string Title,
        string UserRequest);

    private sealed record WorkspaceStepState(
        string Status,
        string? Detail);

    /// <summary>
    /// /workspace/runtime/evaluation-context.json 的内容对应模型。
    /// consumer skill 通过固定 runtime context 路径读取此文件，结构对齐 consumer runtime schema。
    /// </summary>
    private sealed record EvaluationRuntimeContext(
        [property: JsonPropertyName("evaluation_id")] string EvaluationId,
        [property: JsonPropertyName("created_at")] string CreatedAt,
        [property: JsonPropertyName("session")] EvaluationRuntimeContextSession Session,
        [property: JsonPropertyName("employee")] EvaluationRuntimeContextEmployee Employee,
        [property: JsonPropertyName("materials")] EvaluationRuntimeContextMaterials Materials,
        [property: JsonPropertyName("target_sandbox")] EvaluationRuntimeContextTargetSandbox TargetSandbox,
        [property: JsonPropertyName("execution")] EvaluationRuntimeContextExecution Execution,
        [property: JsonPropertyName("hirebot_api")] EvaluationRuntimeContextHireBotApi HireBotApi,
        [property: JsonPropertyName("paths")] EvaluationRuntimeContextPaths Paths,
        [property: JsonPropertyName("runtime_driver")] EvaluationRuntimeContextDriver RuntimeDriver,
        [property: JsonPropertyName("runtime_simulator")] EvaluationRuntimeContextSimulator RuntimeSimulator,
        [property: JsonPropertyName("global_turn_cap")] int GlobalTurnCap);

    /// <summary>
    /// HireBot 业务后端 API 配置，注入到 evaluation-context.json 的 hirebot_api 块。
    /// STEP 3 (run.py) 用 auth.client_credentials 换 WebSocket 所需 token；
    /// STEP 10 (verdict_uploader / trace_uploader) 用相同凭据换 REST API token。
    /// 沙箱内所有出站请求统一通过 auth_client.py 自主换 token，不依赖 C# 注入的静态 token。
    /// </summary>
    private sealed record EvaluationRuntimeContextHireBotApi(
        [property: JsonPropertyName("base_url")] string BaseUrl,
        [property: JsonPropertyName("employee_id")] string EmployeeId,
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("auth")] EvaluationRuntimeContextAuth? Auth);

    /// <summary>
    /// 沙箱自主换 token 所需的 OAuth2 凭据，供 auth_client.py 的 client_credentials 模式使用。
    /// 通过 OpenSandbox:KingCrab:ClientId / ClientSecret / OidcAuthority 配置注入。
    /// 同时用于目标沙箱 WebSocket 连接和 HireBot REST API 调用（同一个 Keycloak realm）。
    /// </summary>
    private sealed record EvaluationRuntimeContextAuth(
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("token_url")] string TokenUrl,
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_secret")] string ClientSecret);

    private sealed record EvaluationRuntimeContextSession(
        [property: JsonPropertyName("session_id")] string SessionId,
        [property: JsonPropertyName("employee_id")] string EmployeeId,
        [property: JsonPropertyName("employee_name")] string EmployeeName,
        [property: JsonPropertyName("iteration")] int Iteration);

    private sealed record EvaluationRuntimeContextEmployee(
        [property: JsonPropertyName("employee_id")] string EmployeeId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("role")] EvaluationRuntimeContextEmployeeRole Role,
        [property: JsonPropertyName("industry")] string Industry,
        [property: JsonPropertyName("job_responsibilities")] string JobResponsibilities,
        [property: JsonPropertyName("scenarios")] IReadOnlyList<string> Scenarios,
        [property: JsonPropertyName("employee_provenance")] EvaluationRuntimeContextEmployeeProvenance EmployeeProvenance,
        [property: JsonPropertyName("source_template_id")] string SourceTemplateId);

    private sealed record EvaluationRuntimeContextEmployeeRole(
        [property: JsonPropertyName("role_id")] string RoleId,
        [property: JsonPropertyName("industry")] string Industry,
        [property: JsonPropertyName("responsibility_tags")] IReadOnlyList<string> ResponsibilityTags);

    private sealed record EvaluationRuntimeContextEmployeeProvenance(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("reliability")] string Reliability,
        [property: JsonPropertyName("caveat")] string Caveat);

    private sealed record EvaluationRuntimeContextMaterials(
        [property: JsonPropertyName("workspace_root")] string WorkspaceRoot,
        [property: JsonPropertyName("consumer_root")] string ConsumerRoot,
        [property: JsonPropertyName("test_cases_dir")] string TestCasesDir,
        [property: JsonPropertyName("ontology_dir")] string OntologyDir);

    private sealed record EvaluationRuntimeContextTargetSandbox(
        [property: JsonPropertyName("sandbox_id")] string SandboxId,
        [property: JsonPropertyName("gateway_endpoint")] string GatewayEndpoint,
        [property: JsonPropertyName("http_base_url")] string HttpBaseUrl);

    private sealed record EvaluationRuntimeContextExecution(
        [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds,
        [property: JsonPropertyName("http_supplement")] bool HttpSupplement);

    private sealed record EvaluationRuntimeContextPaths(
        [property: JsonPropertyName("metrics_dir")] string MetricsDir,
        [property: JsonPropertyName("test_cases_dir")] string TestCasesDir,
        [property: JsonPropertyName("run_dir")] string RunDir,
        [property: JsonPropertyName("drivers_dir")] string DriversDir,
        [property: JsonPropertyName("simulators_dir")] string SimulatorsDir,
        [property: JsonPropertyName("synthesized_cases_dir")] string SynthesizedCasesDir);

    private sealed record EvaluationRuntimeContextDriver(
        [property: JsonPropertyName("driver_id")] string DriverId,
        [property: JsonPropertyName("driver_config")] EvaluationRuntimeContextDriverConfig DriverConfig);

    private sealed record EvaluationRuntimeContextDriverConfig(
        [property: JsonPropertyName("endpoint")] string Endpoint,
        // 当 hirebot_api.auth 已配置 client_credentials 时，token 不写入 JSON（run.py 自行换 token）；
        // 仅在无凭据配置的 fallback 场景下注入静态 token 供 run.py 兜底使用。
        [property: JsonPropertyName("token"), property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token,
        [property: JsonPropertyName("timeout")] int Timeout,
        [property: JsonPropertyName("auto_approve_tools")] bool AutoApproveTools);

    private sealed record EvaluationRuntimeContextSimulator(
        [property: JsonPropertyName("simulator_id")] string SimulatorId);

    private sealed record TargetArtifactWarmupResult(
        string WorkspacePath,
        string SourceArtifactPath);

    private sealed record TargetArtifactBundle(
        string FileName,
        byte[] Content,
        string Sha256,
        string SourceType,
        string SourcePath);

    /// <summary>
    /// 构建写入 /workspace/runtime/evaluation-context.json 的 JSON 内容。
    /// evaluator skill 通过此文件获取会话、材料路径、目标沙箱连接信息以及 HireBot API 配置。
    /// </summary>
    private string BuildRuntimeContextJson(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext ctx,
        EvaluationSessionEntity sessionEntity,
        string targetGatewayEndpoint,
        string materialsWorkspaceDir,
        string targetAccessToken)
    {
        // useTls：只要 Evaluation:GatewayUseTls=true 或 OpenSandbox:Protocol=Https 任一成立，就使用 TLS。
        // 这样生产环境只设置 OpenSandbox:Protocol=Https 也能让 evaluator 得到正确的 wss:// 端点。
        var useTls = configuration.GetValue("Evaluation:GatewayUseTls", false) ||
                     string.Equals(configuration["OpenSandbox:Protocol"], "Https", StringComparison.OrdinalIgnoreCase);

        // Evaluation:ApiBaseUrl 是 NCrew Hire 业务 API 的根地址，供 verdict_uploader.py 上传评估结果。
        // 沙箱内脚本通过 hirebot_api.auth（client_credentials）自主换 token，同时用于 WebSocket 和 REST API。
        var apiBaseUrl = configuration.GetValue("Evaluation:ApiBaseUrl", "http://localhost:5000")!;

        // 读取 KingCrab 凭据，注入到 hirebot_api.auth 供沙箱内脚本自主换 token。
        // 与 KingCrabSandboxTokenProvider 使用相同的配置键，无需额外配置项。
        var oidcAuthority = configuration["OpenSandbox:KingCrab:OidcAuthority"];
        var sandboxClientId = configuration["OpenSandbox:KingCrab:ClientId"];
        var sandboxClientSecret = configuration["OpenSandbox:KingCrab:ClientSecret"];
        EvaluationRuntimeContextAuth? hireBotAuth = null;
        if (!string.IsNullOrWhiteSpace(oidcAuthority) &&
            !string.IsNullOrWhiteSpace(sandboxClientId) &&
            !string.IsNullOrWhiteSpace(sandboxClientSecret))
        {
            var tokenUrl = $"{oidcAuthority.TrimEnd('/')}/protocol/openid-connect/token";
            hireBotAuth = new(Mode: "client_credentials", TokenUrl: tokenUrl, ClientId: sandboxClientId, ClientSecret: sandboxClientSecret);
        }
        var consumerRoot = string.IsNullOrWhiteSpace(materialsWorkspaceDir)
            ? "/workspace/uploads/evaluation-expert-consumer"
            : materialsWorkspaceDir.TrimEnd('/');
        var testCasesDir = $"{consumerRoot}/test-cases";
        var ontologyDir = $"{consumerRoot}/ontology";
        var runDir = $"{consumerRoot}/runs/{NormalizeConsumerPathSegment(sessionEntity.SessionId, "eval")}";
        var globalTurnCap = NormalizeConsumerGlobalTurnCap(configuration.GetValue("Evaluation:ConsumerGlobalTurnCap", 30));
        var driverId = FirstNonEmpty(configuration["Evaluation:ConsumerDriverId"], DefaultEvaluationConsumerDriverId);
        var simulatorId = FirstNonEmpty(configuration["Evaluation:ConsumerSimulatorId"], DefaultEvaluationConsumerSimulatorId);
        var roleId = FirstNonEmpty(employee.RoleName, employee.SourceTemplateId, "unknown-role");
        var scenario = FirstNonEmpty(employee.SourceTemplate, employee.SourceTemplateId, employee.RoleName, "default");
        var targetWsEndpoint = NormalizeGatewayWsEndpoint(targetGatewayEndpoint, useTls);

        var context = new EvaluationRuntimeContext(
            EvaluationId: sessionEntity.SessionId,
            CreatedAt: DateTimeOffset.UtcNow.ToString("o"),
            Session: new(
                SessionId: sessionEntity.SessionId,
                EmployeeId: employee.EmployeeId,
                EmployeeName: employee.Nickname,
                Iteration: sessionEntity.Iteration),
            Employee: new(
                EmployeeId: employee.EmployeeId,
                DisplayName: employee.Nickname,
                Role: new(
                    RoleId: roleId,
                    Industry: string.Empty,
                    ResponsibilityTags: []),
                Industry: string.Empty,
                JobResponsibilities: FirstNonEmpty(employee.StageSummary, employee.CardIntro, employee.SourceTemplate),
                Scenarios: [scenario],
                EmployeeProvenance: ResolveEmployeeProvenance(employee),
                SourceTemplateId: employee.SourceTemplateId),
            Materials: new(
                WorkspaceRoot: "/workspace",
                ConsumerRoot: consumerRoot,
                TestCasesDir: testCasesDir,
                OntologyDir: ontologyDir),
            TargetSandbox: new(
                SandboxId: ctx.TargetSandboxId,
                // WebSocket 入口：确保使用 ws:// 或 wss:// scheme，避免 http→https 308 重定向导致 websockets 库抛 InvalidURI。
                GatewayEndpoint: targetWsEndpoint,
                HttpBaseUrl: NormalizeGatewayHttpBaseUrl(targetGatewayEndpoint, useTls)),
            Execution: new(TimeoutSeconds: 120, HttpSupplement: true),
            HireBotApi: new(
                BaseUrl: apiBaseUrl.TrimEnd('/'),
                EmployeeId: employee.EmployeeId,
                SessionId: sessionEntity.SessionId,
                Auth: hireBotAuth),
            Paths: new(
                MetricsDir: $"{EvaluationConsumerSkillWorkspaceRoot}/metrics",
                TestCasesDir: testCasesDir,
                RunDir: runDir,
                DriversDir: $"{EvaluationConsumerSkillWorkspaceRoot}/runtime-drivers",
                SimulatorsDir: $"{EvaluationConsumerSkillWorkspaceRoot}/simulators",
                SynthesizedCasesDir: $"{consumerRoot}/synthesized-test-cases"),
            RuntimeDriver: new(
                DriverId: driverId,
                DriverConfig: new(
                    Endpoint: targetWsEndpoint,
                    // hireBotAuth 配置后由 run.py 自主换 token，无需注入静态 token；
                    // 仅在凭据缺失时注入 targetAccessToken 作为兜底 fallback。
                    Token: hireBotAuth is not null ? null : targetAccessToken,
                    Timeout: 120,
                    AutoApproveTools: true)),
            RuntimeSimulator: new(SimulatorId: simulatorId),
            GlobalTurnCap: globalTurnCap);

        return JsonSerializer.Serialize(context, JsonOptions);
    }

    /// <summary>
    /// 将 Gateway 节点地址规范化为 HTTP/HTTPS 基础 URL，供 http_client.py 使用。
    /// 支持裸地址、ws:// / wss://、http:// / https:// 四种输入格式。
    /// 当 <paramref name="useTls"/> 为 true 时，http:// 会升级为 https://（处理 DB 中存储的旧格式地址）。
    /// </summary>
    private static string NormalizeGatewayHttpBaseUrl(string endpoint, bool useTls)
    {
        var e = endpoint.Trim().TrimStart('/');
        if (e.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return "https://" + e["wss://".Length..];
        if (e.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            return (useTls ? "https" : "http") + "://" + e["ws://".Length..];
        if (e.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return e;
        if (e.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return useTls ? "https://" + e["http://".Length..] : e;
        // 裸地址：由配置决定是否使用 TLS
        return $"{(useTls ? "https" : "http")}://{e}";
    }

    /// <summary>
    /// 将 Gateway 节点地址规范化为 WebSocket URL（ws:// 或 wss://），供 consumer runtime driver 使用。
    /// 支持裸地址、ws:// / wss://、http:// / https:// 四种输入格式。
    /// 当 <paramref name="useTls"/> 为 true 或输入已含 https:///wss:// 时，强制使用 wss://。
    /// </summary>
    private static string NormalizeGatewayWsEndpoint(string endpoint, bool useTls)
    {
        var e = endpoint.Trim().TrimStart('/');
        if (e.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return e;
        if (e.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            return useTls ? "wss://" + e["ws://".Length..] : e;
        // https:// → 必须用 wss://（TLS 已确定）
        if (e.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "wss://" + e["https://".Length..];
        if (e.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return (useTls ? "wss" : "ws") + "://" + e["http://".Length..];
        // 裸地址
        return $"{(useTls ? "wss" : "ws")}://{e}";
    }

    /// <summary>
    /// 构建发送给 evaluator 沙箱 WebSocket 的启动指令 JSON。
    /// 前端将此字符串作为 user_message.text 发送，驱动 evaluator skill 开始执行。
    /// </summary>
    private static string BuildLiveEvaluationBootstrapPayload(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext ctx,
        EvaluationSessionEntity sessionEntity,
        string targetGatewayEndpoint,
        string runtimeContextPath)
    {
        var payload = new
        {
            workflow = "evaluation_consumer",
            skill_name = EvaluationConsumerSkillName,
            session_id = sessionEntity.SessionId,
            target_hire_id = ctx.TargetHireId,
            evaluator_sandbox_id = ctx.EvaluatorSandboxId,
            target_sandbox_id = ctx.TargetSandboxId,
            runtime_context_path = runtimeContextPath,
            instruction = $"You are the {EvaluationConsumerSkillName} skill running in the evaluation sandbox. " +
                          $"The runtime context has been pre-loaded at {runtimeContextPath}. " +
                          $"Read it first, use runtime_driver.driver_config for the target sandbox connection, " +
                          $"load test cases from paths.test_cases_dir and run artifacts from paths.run_dir, " +
                          $"then execute the deterministic consumer evaluation workflow for employee {employee.EmployeeId} ({employee.Nickname}). " +
                          $"Session ID: {sessionEntity.SessionId}. " +
                          $"Do not invoke the legacy evaluation flow."
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed record ConversationRuntimeContextPayload(
        string RuntimeContextJson,
        string RuntimeContextDefaultPath,
        string TargetGatewayEndpoint,
        string TargetHttpBaseUrl,
        bool MaterialsAttached);

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

