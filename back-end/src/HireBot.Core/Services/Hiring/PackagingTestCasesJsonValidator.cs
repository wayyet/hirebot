using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 打包前评估测试用例 JSON 的转录过滤、校验与元数据追加（供 Skill 回调解析与单测复用）。
/// </summary>
internal static partial class PackagingTestCasesJsonValidator
{
    private const int MaxHistoryTurns = 40;
    private const int MaxHistoryCharacters = 12_000;
    private const int MinUserMessageLength = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    [GeneratedRegex(
        @"生成(?:实例|产物)?包|开始(?:生成)?打包|产物包|template_package|package_workspace|ready_for_packaging|instance_packaging",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex PackagingIntentRegex();

    internal static IReadOnlyList<HistoryTranscriptTurn> PrepareHistoryTranscript(
        IReadOnlyList<HiringConversationMessageDto> messages)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var filtered = new List<HistoryTranscriptTurn>();
        var totalCharacters = 0;

        foreach (var message in messages)
        {
            var role = message.Role.Trim();
            if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = message.Content?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                if (content.Length < MinUserMessageLength || PackagingIntentRegex().IsMatch(content))
                {
                    continue;
                }
            }

            filtered.Add(new HistoryTranscriptTurn(role.ToLowerInvariant(), content));
            totalCharacters += content.Length;
        }

        if (filtered.Count > MaxHistoryTurns)
        {
            filtered = filtered.Skip(filtered.Count - MaxHistoryTurns).ToList();
        }

        while (filtered.Count > 0 && totalCharacters > MaxHistoryCharacters)
        {
            totalCharacters -= filtered[0].Content.Length;
            filtered.RemoveAt(0);
        }

        return filtered;
    }

    internal static bool TryValidateTestCasesJson(string json, out string normalizedJson)
    {
        normalizedJson = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("test_cases", out var testCasesElement) ||
                testCasesElement.ValueKind != JsonValueKind.Array ||
                testCasesElement.GetArrayLength() == 0)
            {
                return false;
            }

            foreach (var testCase in testCasesElement.EnumerateArray())
            {
                if (testCase.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryGetNonEmptyString(testCase, out _, "test_case_id", "testcase_id"))
                {
                    return false;
                }

                if (!TryGetNonEmptyString(testCase, out _, "scenario_name", "title"))
                {
                    return false;
                }

                if (!testCase.TryGetProperty("input", out var inputElement) ||
                    inputElement.ValueKind != JsonValueKind.Object ||
                    !TryGetNonEmptyString(inputElement, out _, "user_request"))
                {
                    return false;
                }
            }

            normalizedJson = PackagingTestCasesJsonFormatting.FormatAsHumanReadableJson(root);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 允许 test_cases 为空数组（降级占位 JSON）。
    /// </summary>
    internal static bool TryValidateFallbackTestCasesJson(string json, out string normalizedJson)
    {
        normalizedJson = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("test_cases", out var testCasesElement) ||
                testCasesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            normalizedJson = json.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string AppendPackagingMetadata(string testCasesJson, string source)
    {
        using var document = JsonDocument.Parse(testCasesJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, PackagingTestCasesJsonFormatting.HumanReadableWriterOptions))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteString("generated_at", DateTimeOffset.UtcNow);
            writer.WriteString("source", source);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static bool TryExtractEvaluationTestCasesJson(
        HiringDispatchCallbackPayload callback,
        out string testCasesJson,
        out string source)
    {
        if (TryExtractPackagingTestCasesBundle(callback, out var bundle))
        {
            testCasesJson = bundle.MergedJson;
            source = bundle.Source;
            return true;
        }

        testCasesJson = string.Empty;
        source = string.Empty;
        return false;
    }

    internal static bool TryExtractPackagingTestCasesBundle(
        HiringDispatchCallbackPayload callback,
        out PackagingTestCasesBundle bundle)
    {
        bundle = default!;
        if (callback.TechnicalArtifact is not { } artifact ||
            artifact.ValueKind != JsonValueKind.Object)
        {
            return TryExtractLegacyMergedOnly(callback, out bundle);
        }

        var source = artifact.TryGetProperty("source", out var sourceElement) &&
                     sourceElement.ValueKind == JsonValueKind.String
            ? sourceElement.GetString()?.Trim() ?? string.Empty
            : string.Empty;

        if (!TryReadArtifactString(artifact, "evaluation_test_cases_json", out var mergedJson))
        {
            return TryExtractLegacyMergedOnly(callback, out bundle);
        }

        var hasIndex = TryReadArtifactString(artifact, "testcases_sources_index_json", out var indexJson);
        var hasHistory = TryReadArtifactString(artifact, "history_derived_json", out var historyJson);
        var hasMaterials = TryReadArtifactString(artifact, "materials_derived_json", out var materialsJson);
        var hasTemplate = TryReadArtifactString(artifact, "template_derived_json", out var templateJson);

        if (hasIndex && hasHistory && hasMaterials && hasTemplate &&
            TryValidateTestCasesJson(mergedJson, out var normalizedMerged) &&
            TryValidateSourcesIndexJson(indexJson, out var normalizedIndex) &&
            TryValidateDerivedTestCasesJson(historyJson, out var normalizedHistory) &&
            TryValidateDerivedTestCasesJson(materialsJson, out var normalizedMaterials) &&
            TryValidateDerivedTestCasesJson(templateJson, out var normalizedTemplate))
        {
            bundle = new PackagingTestCasesBundle(
                normalizedMerged,
                normalizedIndex,
                normalizedHistory,
                normalizedMaterials,
                normalizedTemplate,
                string.IsNullOrWhiteSpace(source) ? "packaging-merged" : source);
            return true;
        }

        if (TryValidateTestCasesJson(mergedJson, out normalizedMerged))
        {
            bundle = new PackagingTestCasesBundle(
                normalizedMerged,
                SourcesIndexJson: string.Empty,
                HistoryDerivedJson: string.Empty,
                MaterialsDerivedJson: string.Empty,
                TemplateDerivedJson: string.Empty,
                string.IsNullOrWhiteSpace(source) ? "kingcrab-history-llm" : source);
            return true;
        }

        if (string.Equals(source, "packaging-fallback", StringComparison.OrdinalIgnoreCase) &&
            TryValidateFallbackTestCasesJson(mergedJson, out normalizedMerged))
        {
            bundle = new PackagingTestCasesBundle(
                normalizedMerged,
                SourcesIndexJson: string.Empty,
                HistoryDerivedJson: string.Empty,
                MaterialsDerivedJson: string.Empty,
                TemplateDerivedJson: string.Empty,
                source);
            return true;
        }

        return false;
    }

    internal static bool TryValidateSourcesIndexJson(string json, out string normalizedJson)
    {
        normalizedJson = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("primary", out var primaryElement) ||
                primaryElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(primaryElement.GetString()) ||
                !root.TryGetProperty("sources", out var sourcesElement) ||
                sourcesElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            normalizedJson = json.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 来源子文件允许 test_cases 为空数组。
    /// </summary>
    internal static bool TryValidateDerivedTestCasesJson(string json, out string normalizedJson)
    {
        normalizedJson = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("test_cases", out var testCasesElement) ||
                testCasesElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var testCase in testCasesElement.EnumerateArray())
            {
                if (testCase.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryGetNonEmptyString(testCase, out _, "test_case_id", "testcase_id") ||
                    !TryGetNonEmptyString(testCase, out _, "scenario_name", "title") ||
                    !testCase.TryGetProperty("input", out var inputElement) ||
                    inputElement.ValueKind != JsonValueKind.Object ||
                    !TryGetNonEmptyString(inputElement, out _, "user_request"))
                {
                    return false;
                }
            }

            normalizedJson = json.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractLegacyMergedOnly(
        HiringDispatchCallbackPayload callback,
        out PackagingTestCasesBundle bundle)
    {
        bundle = default!;
        foreach (var artifactPayload in callback.Artifacts)
        {
            if (!artifactPayload.Path.Contains("evaluation-test-cases", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(artifactPayload.Content))
            {
                continue;
            }

            var mergedJson = HiringWorkflowSupport.DecodeArtifactContent(artifactPayload) is { Length: > 0 } bytes
                ? Encoding.UTF8.GetString(bytes)
                : artifactPayload.Content.Trim();
            if (string.IsNullOrWhiteSpace(mergedJson))
            {
                return false;
            }

            if (TryValidateTestCasesJson(mergedJson, out var normalizedMerged))
            {
                bundle = new PackagingTestCasesBundle(
                    normalizedMerged,
                    SourcesIndexJson: string.Empty,
                    HistoryDerivedJson: string.Empty,
                    MaterialsDerivedJson: string.Empty,
                    TemplateDerivedJson: string.Empty,
                    "kingcrab-history-llm");
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryReadArtifactString(JsonElement artifact, string propertyName, out string value)
    {
        value = string.Empty;
        if (!artifact.TryGetProperty(propertyName, out var jsonElement) ||
            jsonElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = jsonElement.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    internal static string SerializeInvokePayload(PackagingTestCasesInvokePayload payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    private static bool TryGetNonEmptyString(JsonElement element, out string value, params string[] propertyNames)
    {
        value = string.Empty;
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal sealed record HistoryTranscriptTurn(string Role, string Content);
}

internal sealed record PackagingTestCasesInvokePayload(
    string SessionId,
    string TemplateName,
    IReadOnlyDictionary<string, string?> StructuredData,
    IReadOnlyList<PackagingTestCasesJsonValidator.HistoryTranscriptTurn> HistoryMessages,
    IReadOnlyList<PackagingMaterialFileSnapshot> UploadedMaterialFiles,
    IReadOnlyList<PackagingTemplateFileSnapshot> TemplatePackageFiles);

internal sealed record PackagingTestCasesBundle(
    string MergedJson,
    string SourcesIndexJson,
    string HistoryDerivedJson,
    string MaterialsDerivedJson,
    string TemplateDerivedJson,
    string Source);
