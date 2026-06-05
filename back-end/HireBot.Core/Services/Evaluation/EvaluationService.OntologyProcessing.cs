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
    private async Task<OntologyProfile> BuildOntologyProfileAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var sources = await LoadOntologySourcesAsync(workspaceContext, employee, cancellationToken);
        var rules = BuildOntologyRulesFromSources(sources);
        var normalizedRules = rules.Count == 0
            ? DefaultOntologyRules.ToArray()
            : rules;

        var sourceSummary = sources.Count == 0
            ? "default-ontology"
            : string.Join(
                ",",
                sources
                    .Select(item => item.SourceType)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

        return new OntologyProfile(
            DimensionWeights: new Dictionary<string, decimal>(DefaultOntologyWeights, StringComparer.OrdinalIgnoreCase),
            DimensionRules: normalizedRules,
            Sources: sources,
            SourceSummary: sourceSummary);
    }

    private async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var fromTarget = await LoadOntologySourcesFromTargetArtifactsAsync(employee.EmployeeId, cancellationToken);
        if (fromTarget.Count > 0)
        {
            return fromTarget;
        }

        var templateHints = BuildTemplateHints(employee);

        var hintsWithEmployeeId = new List<string>(templateHints) { employee.EmployeeId };
        if (employee.EmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
            hintsWithEmployeeId.Add($"hire_{employee.EmployeeId[2..]}");

        return await LoadOntologySourcesFromFixtureAsync(
            workspaceContext.TargetHireId,
            hintsWithEmployeeId,
            cancellationToken);
    }

    private async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesFromTargetArtifactsAsync(
        string targetHireId,
        CancellationToken cancellationToken)
    {
        // targetHireId 实际传入的是 employee.EmployeeId，通过反向索引查 hireId
        var packageSnapshot = await artifactPackageService.GetLatestPackageByEmployeeIdAsync(targetHireId, cancellationToken);
        if (packageSnapshot?.Content is not { Length: > 0 })
        {
            return [];
        }

        var sources = new List<OntologySourceFile>();
        try
        {
            using var stream = new MemoryStream(packageSnapshot.Content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                var normalizedPath = entry.FullName.Replace('\\', '/');
                if (!normalizedPath.StartsWith("ontology/", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith("ontology/hiring-session/", StringComparison.OrdinalIgnoreCase) ||
                    !IsOntologyFileExtension(normalizedPath))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                var content = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                sources.Add(new OntologySourceFile(
                    FileName: Path.GetFileName(normalizedPath),
                    SourcePath: normalizedPath,
                    Content: content,
                    SourceType: packageSnapshot.Kind));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract ontology files from target artifacts. TargetHireId={TargetHireId}", targetHireId);
        }

        return sources;
    }

    private static async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesFromFixtureAsync(
        string targetHireId,
        IReadOnlyList<string> templateHints,
        CancellationToken cancellationToken)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return [];
        }

        var scopedRoot = ResolveScopedFixtureOntologyRoot(fixtureRoot, targetHireId);
        var scopedSources = await LoadOntologySourcesFromDirectoryAsync(scopedRoot, "fixture-scoped", cancellationToken);
        if (scopedSources.Count > 0)
        {
            return scopedSources;
        }

        var templateScopedRoots = ResolveTemplateScopedFixtureOntologyRoots(fixtureRoot, templateHints);
        foreach (var templateScopedRoot in templateScopedRoots)
        {
            var templateScopedSources = await LoadOntologySourcesFromDirectoryAsync(
                templateScopedRoot,
                "fixture-template-scoped",
                cancellationToken);
            if (templateScopedSources.Count > 0)
            {
                return templateScopedSources;
            }
        }

        var globalRoot = Path.Combine(fixtureRoot, "ontology");
        return await LoadOntologySourcesFromDirectoryAsync(globalRoot, "fixture-global", cancellationToken);
    }

    private static async Task<IReadOnlyList<OntologySourceFile>> LoadOntologySourcesFromDirectoryAsync(
        string? sourceDirectory,
        string sourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return [];
        }

        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsOntologyFileExtension)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        var sources = new List<OntologySourceFile>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            sources.Add(new OntologySourceFile(
                FileName: Path.GetFileName(file),
                SourcePath: file,
                Content: content,
                SourceType: sourceType));
        }

        return sources;
    }

    private static bool IsOntologyFileExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildOntologyRulesFromSources(IReadOnlyList<OntologySourceFile> sources)
    {
        if (sources.Count == 0)
        {
            return [];
        }

        var candidates = new List<OntologyRuleCandidate>();
        foreach (var source in sources)
        {
            if (LooksLikeJson(source.Content))
            {
                candidates.AddRange(ParseOntologyRuleCandidatesFromJson(source));
            }
            else
            {
                candidates.AddRange(ParseOntologyRuleCandidatesFromMarkdown(source));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var selectedRules = new List<string>();
        var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dimension in DefaultOntologyWeights.Keys)
        {
            var candidate = candidates.FirstOrDefault(item =>
                item.Dimension.Equals(dimension, StringComparison.OrdinalIgnoreCase));
            if (candidate is null || !seenTexts.Add(candidate.Text))
            {
                continue;
            }

            selectedRules.Add(
                $"{ToDimensionDisplayName(dimension)}: {candidate.Text} (source: {candidate.SourceFile})");
        }

        foreach (var candidate in candidates)
        {
            if (selectedRules.Count >= 10 || !seenTexts.Add(candidate.Text))
            {
                continue;
            }

            selectedRules.Add(
                $"{ToDimensionDisplayName(candidate.Dimension)}: {candidate.Text} (source: {candidate.SourceFile})");
        }

        foreach (var defaultRule in DefaultOntologyRules)
        {
            if (selectedRules.Count >= 10)
            {
                break;
            }

            var defaultRuleKey = defaultRule.Split(':', 2)[0].Trim();
            var alreadyCovered = selectedRules.Any(rule =>
                rule.StartsWith(defaultRuleKey + ":", StringComparison.OrdinalIgnoreCase));
            if (!alreadyCovered)
            {
                selectedRules.Add(defaultRule);
            }
        }

        return selectedRules;
    }

    private static IReadOnlyList<OntologyRuleCandidate> ParseOntologyRuleCandidatesFromMarkdown(OntologySourceFile source)
    {
        var rules = new List<OntologyRuleCandidate>();
        var section = string.Empty;
        foreach (var rawLine in source.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith('#'))
            {
                section = rawLine.TrimStart('#').Trim();
                continue;
            }

            var text = TryExtractListItem(rawLine);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 6)
            {
                continue;
            }

            rules.Add(new OntologyRuleCandidate(
                Dimension: InferOntologyDimension(section, text),
                Text: text,
                SourceFile: source.FileName));
        }

        return rules;
    }

    private static IReadOnlyList<OntologyRuleCandidate> ParseOntologyRuleCandidatesFromJson(OntologySourceFile source)
    {
        try
        {
            using var document = JsonDocument.Parse(source.Content);
            var rules = new List<OntologyRuleCandidate>();
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return rules;
            }

            if (root.TryGetProperty("rules", out var rulesElement) &&
                rulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var ruleElement in rulesElement.EnumerateArray())
                {
                    if (ruleElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var text = ruleElement.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    rules.Add(new OntologyRuleCandidate(
                        Dimension: InferOntologyDimension(string.Empty, text),
                        Text: text,
                        SourceFile: source.FileName));
                }
            }

            if (root.TryGetProperty("dimensionRules", out var dimensionRulesElement) &&
                dimensionRulesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in dimensionRulesElement.EnumerateObject())
                {
                    var dimension = NormalizeOntologyDimension(property.Name);
                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                        {
                            var text = property.Value.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                rules.Add(new OntologyRuleCandidate(dimension, text, source.FileName));
                            }

                            break;
                        }
                        case JsonValueKind.Array:
                        {
                            foreach (var ruleElement in property.Value.EnumerateArray())
                            {
                                if (ruleElement.ValueKind != JsonValueKind.String)
                                {
                                    continue;
                                }

                                var text = ruleElement.GetString()?.Trim();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    rules.Add(new OntologyRuleCandidate(dimension, text, source.FileName));
                                }
                            }

                            break;
                        }
                    }
                }
            }

            if (root.TryGetProperty("dimensions", out var dimensionsElement) &&
                dimensionsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in dimensionsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (property.Value.TryGetProperty("rule", out var ruleElement) &&
                        ruleElement.ValueKind == JsonValueKind.String)
                    {
                        var text = ruleElement.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            rules.Add(new OntologyRuleCandidate(
                                NormalizeOntologyDimension(property.Name),
                                text,
                                source.FileName));
                        }
                    }

                    if (property.Value.TryGetProperty("description", out var descriptionElement) &&
                        descriptionElement.ValueKind == JsonValueKind.String)
                    {
                        var text = descriptionElement.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            rules.Add(new OntologyRuleCandidate(
                                NormalizeOntologyDimension(property.Name),
                                text,
                                source.FileName));
                        }
                    }
                }
            }

            return rules;
        }
        catch
        {
            return [];
        }
    }

    private static string? TryExtractListItem(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
        {
            return trimmed[2..].Trim();
        }

        var separatorIndex = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (separatorIndex <= 0 || !int.TryParse(trimmed[..separatorIndex], out _))
        {
            return null;
        }

        return trimmed[(separatorIndex + 2)..].Trim();
    }

    private static bool LooksLikeJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            return ch is '{' or '[';
        }

        return false;
    }

    private static string InferOntologyDimension(string section, string text)
    {
        var normalized = $"{section} {text}".ToLowerInvariant();
        if (ContainsAny(normalized, "compliance", "constraint", "policy", "approval", "approve", "sign-off", "risk", "合规", "约束", "审批", "必须", "管控"))
        {
            return "compliance";
        }

        if (ContainsAny(normalized, "communication", "clear", "polite", "actionable", "沟通", "表达", "易读", "清晰"))
        {
            return "communication";
        }

        if (ContainsAny(normalized, "accuracy", "entity", "fact", "domain", "context", "精准", "准确", "实体"))
        {
            return "accuracy";
        }

        if (ContainsAny(normalized, "complete", "completeness", "action", "step", "workflow", "lifecycle", "流程", "步骤", "闭环", "全生命周期", "任务"))
        {
            return "completeness";
        }

        return "completeness";
    }

    private static string NormalizeOntologyDimension(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "completeness";
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.Contains("accuracy") || normalized.Contains("准确"))
        {
            return "accuracy";
        }

        if (normalized.Contains("complete") || normalized.Contains("completeness") || normalized.Contains("完整"))
        {
            return "completeness";
        }

        if (normalized.Contains("compliance") || normalized.Contains("合规"))
        {
            return "compliance";
        }

        if (normalized.Contains("communication") || normalized.Contains("沟通"))
        {
            return "communication";
        }

        return normalized;
    }

    private static string ToDimensionDisplayName(string dimension)
    {
        var normalized = NormalizeOntologyDimension(dimension);
        return normalized switch
        {
            "accuracy" => "Accuracy",
            "completeness" => "Completeness",
            "compliance" => "Compliance",
            "communication" => "Communication",
            _ => normalized
        };
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) &&
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
