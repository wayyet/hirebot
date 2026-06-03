using System.Text;
using System.Text.Json;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 合并 final 包中已有的兜底测试用例与 staging（intermediate/WTP）产物，避免整文件覆盖。
/// </summary>
internal static class PackagingTestCasesJsonMerger
{
    /// <summary>
    /// 将 existing（merged 中已有，可能为兜底）与 staged（packaging 主数据）合并为统一 test_cases 结构。
    /// staged 条目优先；按 test_case_id / caseId 去重。
    /// </summary>
    internal static bool TryMergeEvaluationTestCasesJson(
        string? existingJson,
        string stagedJson,
        out string mergedJson)
    {
        mergedJson = string.Empty;
        if (string.IsNullOrWhiteSpace(stagedJson))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(existingJson))
        {
            mergedJson = stagedJson.Trim();
            return true;
        }

        try
        {
            using var stagedDoc = JsonDocument.Parse(stagedJson);
            using var existingDoc = JsonDocument.Parse(existingJson);
            var stagedRoot = stagedDoc.RootElement;
            var existingRoot = existingDoc.RootElement;

            if (stagedRoot.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var mergedCases = new List<JsonElement>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // staged 优先
            AppendCases(stagedRoot, mergedCases, seenIds);
            AppendCases(existingRoot, mergedCases, seenIds);

            if (mergedCases.Count == 0 &&
                !HasAnyCaseArray(stagedRoot) &&
                !HasAnyCaseArray(existingRoot))
            {
                mergedJson = stagedJson.Trim();
                return true;
            }

            var mergedSources = CollectSources(stagedRoot, existingRoot);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, PackagingTestCasesJsonFormatting.HumanReadableWriterOptions))
            {
                writer.WriteStartObject();
                CopyMetadataProperties(stagedRoot, writer, skipKeys: ["test_cases", "cases", "merged_sources"]);
                if (mergedSources.Count > 0)
                {
                    writer.WritePropertyName("merged_sources");
                    writer.WriteStartArray();
                    foreach (var source in mergedSources)
                    {
                        writer.WriteStringValue(source);
                    }

                    writer.WriteEndArray();
                }

                writer.WritePropertyName("test_cases");
                writer.WriteStartArray();
                foreach (var testCase in mergedCases)
                {
                    testCase.WriteTo(writer);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            mergedJson = Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
        catch (JsonException)
        {
            mergedJson = stagedJson.Trim();
            return true;
        }
    }

    private static void AppendCases(JsonElement root, List<JsonElement> target, HashSet<string> seenIds)
    {
        foreach (var propertyName in new[] { "test_cases", "cases" })
        {
            if (!root.TryGetProperty(propertyName, out var casesElement) ||
                casesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var testCase in casesElement.EnumerateArray())
            {
                if (testCase.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = ResolveCaseId(testCase);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    if (!seenIds.Add(id))
                    {
                        continue;
                    }
                }
                else
                {
                    var fingerprint = testCase.GetRawText();
                    if (!seenIds.Add($"__anon__:{fingerprint}"))
                    {
                        continue;
                    }
                }

                target.Add(testCase.Clone());
            }
        }
    }

    private static bool HasAnyCaseArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var propertyName in new[] { "test_cases", "cases" })
        {
            if (root.TryGetProperty(propertyName, out var casesElement) &&
                casesElement.ValueKind == JsonValueKind.Array &&
                casesElement.GetArrayLength() > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectSources(JsonElement stagedRoot, JsonElement existingRoot)
    {
        var sources = new List<string>();
        TryAddSource(stagedRoot, sources);
        TryAddSource(existingRoot, sources);
        return sources;
    }

    private static void TryAddSource(JsonElement root, List<string> sources)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("source", out var sourceElement) ||
            sourceElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var value = sourceElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!sources.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            sources.Add(value);
        }
    }

    private static void CopyMetadataProperties(
        JsonElement sourceRoot,
        Utf8JsonWriter writer,
        IReadOnlyCollection<string> skipKeys)
    {
        foreach (var property in sourceRoot.EnumerateObject())
        {
            if (skipKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            property.WriteTo(writer);
        }
    }

    private static string? ResolveCaseId(JsonElement testCase)
    {
        foreach (var name in new[] { "test_case_id", "testcase_id", "caseId", "case_id" })
        {
            if (testCase.TryGetProperty(name, out var idElement) &&
                idElement.ValueKind == JsonValueKind.String)
            {
                var value = idElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
