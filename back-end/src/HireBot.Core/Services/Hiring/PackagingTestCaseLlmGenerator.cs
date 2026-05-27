using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireBot.Abstraction.Models.Hiring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class PackagingTestCaseLlmGenerator(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<PackagingTestCaseLlmGenerator> logger) : IPackagingTestCaseLlmGenerator
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

    private const string SchemaExample = """
        {
          "description": "电商客服专员评估测试用例 — Demo",
          "role": "customer_service",
          "industry": "ecommerce",
          "test_cases": [
            {
              "test_case_id": "TC-001",
              "scenario_name": "商品质量问题退货申请",
              "input": {
                "user_request": "你好，我上周买的商品收到后发现质量有问题...",
                "context": { "order_id": "ORD-20260415-88888" }
              },
              "expected_behavior_sequence": [
                { "step": 1, "action": "安抚用户情绪", "criteria": "语气友好，表达同理心" }
              ],
              "expected_output": {
                "resolution": "退货申请已受理",
                "user_satisfaction": "用户满意",
                "artifacts_created": ["退货工单"]
              }
            }
          ]
        }
        """;

    public async Task<(bool Success, string Json)> TryGenerateAsync(
        PackagingTestCaseGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = configuration["OpenSandbox:KingCrab:LlmModel"];
        var endpoint = configuration["OpenSandbox:KingCrab:LlmEndpoint"];
        var apiKey = configuration["OpenSandbox:KingCrab:LlmApiKey"];
        if (string.IsNullOrWhiteSpace(model) ||
            string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("[Hiring] Packaging testcase LLM config is incomplete.");
            return (false, string.Empty);
        }

        var transcript = PrepareHistoryTranscript(request.HistoryMessages);
        if (transcript.Count == 0)
        {
            logger.LogWarning("[Hiring] Packaging testcase LLM skipped because history transcript is empty.");
            return (false, string.Empty);
        }

        var structuredSummary = BuildStructuredDataSummary(request.StructuredData);
        var systemPrompt = """
            你是数字员工评估测试用例编写专家。根据雇佣对话历史，生成用于 live_evaluator 的 JSON 测试用例文件。
            要求：
            1. 仅输出合法 JSON，不要 Markdown 代码块或解释文字。
            2. 顶层字段必须包含 description、role、industry、test_cases。
            3. test_cases 生成 3 到 8 条，每条含 test_case_id、scenario_name、input.user_request、input.context、expected_behavior_sequence、expected_output。
            4. expected_behavior_sequence 至少 2 步，含 step/action/criteria。
            5. 从真实对话提炼业务场景，忽略打包/生成实例包/系统 priming 类消息。
            6. test_case_id 使用 TC-001、TC-002 递增格式。
            """;

        var userPrompt = $"""
            模板名称：{request.TemplateName}
            结构化业务字段：
            {structuredSummary}

            对话历史（JSON 数组，role 为 user 或 assistant）：
            {JsonSerializer.Serialize(transcript, JsonOptions)}

            输出 JSON 结构示例（字段名与嵌套必须一致，内容需基于上述对话重写）：
            {SchemaExample}
            """;

        var temperature = configuration.GetValue("OpenSandbox:KingCrab:LlmTemperature", 0.7f);
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model.Trim(),
            ["temperature"] = temperature,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());

            var completionsUrl = BuildChatCompletionsUrl(endpoint);
            using var response = await client.PostAsJsonAsync(completionsUrl, requestBody, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "[Hiring] Packaging testcase LLM HTTP failed. StatusCode={StatusCode}, BodyPreview={BodyPreview}",
                    (int)response.StatusCode,
                    Truncate(errorBody, 500));
                return (false, string.Empty);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var responseDoc = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var content = ExtractAssistantContent(responseDoc.RootElement);
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("[Hiring] Packaging testcase LLM returned empty content.");
                return (false, string.Empty);
            }

            var normalizedJson = NormalizeLlmJsonContent(content);
            if (!TryValidateTestCasesJson(normalizedJson, out var validatedJson))
            {
                logger.LogWarning(
                    "[Hiring] Packaging testcase LLM output failed validation. BodyPreview={BodyPreview}",
                    Truncate(normalizedJson, 500));
                return (false, string.Empty);
            }

            return (true, validatedJson);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "[Hiring] Packaging testcase LLM call failed.");
            return (false, string.Empty);
        }
    }

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

            var index = 0;
            foreach (var testCase in testCasesElement.EnumerateArray())
            {
                index++;
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
        using (var writer = new Utf8JsonWriter(stream))
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

    private static string BuildStructuredDataSummary(IReadOnlyDictionary<string, string?> structuredData)
    {
        if (structuredData.Count == 0)
        {
            return "(无)";
        }

        var lines = structuredData
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"- {pair.Key}: {pair.Value!.Trim()}")
            .Take(20);
        return string.Join('\n', lines);
    }

    private static string BuildChatCompletionsUrl(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        return normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}/chat/completions";
    }

    private static string? ExtractAssistantContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message))
            {
                continue;
            }

            if (message.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String)
            {
                return contentElement.GetString();
            }
        }

        return null;
    }

    private static string NormalizeLlmJsonContent(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }
        }

        return trimmed.Trim();
    }

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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    internal sealed record HistoryTranscriptTurn(string Role, string Content);
}
