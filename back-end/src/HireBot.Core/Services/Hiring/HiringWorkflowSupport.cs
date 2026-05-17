using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Core.Services.Hiring;

internal static partial class HiringWorkflowSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static ParsedHiringAssistantReply ParseAssistantReply(string content)
    {
        var normalizedContent = content ?? string.Empty;
        var dispatchCommands = new List<HiringDispatchCommand>();
        var dispatchCallbacks = new List<HiringDispatchCallbackPayload>();
        var configFiles = new List<HiringConfigGovernanceFileDto>();
        HiringDiagnosticReportDto? diagnosticReport = null;

        foreach (Match match in HiringTagRegex().Matches(normalizedContent))
        {
            var tagName = match.Groups["tag"].Value.ToLowerInvariant();
            var tagContent = StripCodeFences(match.Groups["content"].Value.Trim());
            if (string.IsNullOrWhiteSpace(tagContent))
            {
                continue;
            }

            switch (tagName)
            {
                case "dispatch":
                    if (TryDeserialize<HiringDispatchCommand>(tagContent) is { } command)
                    {
                        dispatchCommands.Add(command);
                    }

                    break;
                case "dispatch_callback":
                    if (TryDeserialize<HiringDispatchCallbackPayload>(tagContent) is { } callback)
                    {
                        dispatchCallbacks.Add(callback);
                    }

                    break;
                case "diagnostic_report":
                    diagnosticReport = TryDeserialize<HiringDiagnosticReportDto>(tagContent);
                    break;
                case "config_governance_patch":
                    if (TryDeserialize<HiringConfigGovernancePatchDocument>(tagContent) is { } patch)
                    {
                        configFiles.AddRange(patch.Files);
                    }

                    break;
            }
        }

        var visibleContent = HiringTagRegex().Replace(normalizedContent, string.Empty).Trim();
        return new ParsedHiringAssistantReply(
            string.IsNullOrWhiteSpace(visibleContent) ? "已处理当前编排事件。" : visibleContent,
            dispatchCommands,
            dispatchCallbacks,
            diagnosticReport,
            configFiles);
    }

    public static byte[] DecodeArtifactContent(HiringDispatchCallbackArtifactPayload artifact)
    {
        if (string.Equals(artifact.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.FromBase64String(artifact.Content ?? string.Empty);
        }

        return Encoding.UTF8.GetBytes(artifact.Content ?? string.Empty);
    }

    public static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static bool ContainsSensitiveValue(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return SensitiveValueRegex().IsMatch(content);
    }

    public static bool IsAllowedArtifactPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith("ontology/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("skills/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("external/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("config/", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripCodeFences(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith('`'))
        {
            var fenceLength = trimmed.StartsWith("```", StringComparison.Ordinal) ? 3 : 1;
            var afterFence = trimmed[fenceLength..];
            var languageLength = 0;
            while (languageLength < afterFence.Length && char.IsLetterOrDigit(afterFence[languageLength]))
            {
                languageLength++;
            }

            afterFence = afterFence[languageLength..];
            if (afterFence.StartsWith('\n'))
            {
                afterFence = afterFence[1..];
            }

            var trailingFence = new string('`', fenceLength);
            if (afterFence.EndsWith(trailingFence, StringComparison.Ordinal))
            {
                var body = afterFence.TrimEnd();
                if (body.EndsWith(trailingFence, StringComparison.Ordinal))
                {
                    body = body[..^fenceLength].TrimEnd();
                }

                afterFence = body;
            }

            trimmed = afterFence;
        }

        return trimmed.Trim();
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("<(?<tag>dispatch|dispatch_callback|diagnostic_report|config_governance_patch)>(?<content>[\\s\\S]*?)</\\k<tag>>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HiringTagRegex();

    [GeneratedRegex("(token|api[_-]?key|secret|password|connection[_-]?string)\\s*[:=]\\s*[\"']?[A-Za-z0-9_\\-:/+=]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SensitiveValueRegex();
}

internal sealed record ParsedHiringAssistantReply(
    string VisibleContent,
    IReadOnlyList<HiringDispatchCommand> DispatchCommands,
    IReadOnlyList<HiringDispatchCallbackPayload> DispatchCallbacks,
    HiringDiagnosticReportDto? DiagnosticReport,
    IReadOnlyList<HiringConfigGovernanceFileDto> ConfigGovernanceFiles);

internal sealed record HiringDispatchCommand(
    string Target,
    IReadOnlyList<string> HandoffIds,
    string? To,
    string? Note,
    string? Mode);

internal sealed record HiringDispatchCallbackArtifactPayload(
    string Path,
    string Kind,
    string Encoding,
    string? Content,
    string Sha256);

internal sealed record HiringDispatchCallbackTodoResultPayload(
    string HandoffId,
    string Status,
    IReadOnlyList<HiringDispatchCallbackArtifactPayload> Artifacts,
    IReadOnlyList<HiringCredentialSlotDto>? CredentialSlots,
    IReadOnlyList<string> Errors);

internal sealed record HiringDispatchCallbackPayload(
    string SourceDispatchTarget,
    IReadOnlyList<string> HandoffIds,
    string UserSummary,
    IReadOnlyList<HiringDispatchCallbackArtifactPayload> Artifacts,
    IReadOnlyList<HiringDispatchCallbackTodoResultPayload> TodoResults,
    string Status,
    IReadOnlyList<string> Errors);

internal sealed record HiringConfigGovernancePatchDocument(
    IReadOnlyList<HiringConfigGovernanceFileDto> Files);
