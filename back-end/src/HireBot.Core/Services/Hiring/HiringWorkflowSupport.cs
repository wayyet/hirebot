using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;

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

    public static HiringDiagnosticReportDto EvaluateDiagnosis(HiringRuntimeContext runtimeContext)
    {
        var handoffItems = runtimeContext.HandoffItems
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.HandoffId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var stageReadiness = new[]
        {
            BuildMaterialStageReadiness(runtimeContext),
            BuildSkillStageReadiness(runtimeContext),
            BuildExternalStageReadiness(runtimeContext)
        };

        var diagnosticTodos = new List<HiringDiagnosticTodoDto>();
        foreach (var readiness in stageReadiness)
        {
            if (string.Equals(readiness.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(readiness.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            diagnosticTodos.Add(new HiringDiagnosticTodoDto(
                Id: $"d_{readiness.Stage}",
                Stage: readiness.Stage,
                Level: HiringDiagnosticPriority.Required,
                Category: "stage_readiness",
                Question: BuildDiagnosticQuestion(readiness.Stage),
                Evidence: readiness.Reason,
                SuggestedAction: BuildDiagnosticAction(readiness.Stage),
                RelatedHandoffIds: readiness.BlockingHandoffIds));
        }

        var needsReviewHandoffIds = handoffItems
            .Where(item => string.Equals(item.Status, HiringHandoffStatus.NeedsReview, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.HandoffId)
            .Concat(runtimeContext.ConfigGovernance?.PendingReviewHandoffIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (needsReviewHandoffIds.Length > 0)
        {
            diagnosticTodos.Add(new HiringDiagnosticTodoDto(
                Id: "d_cross_stage_needs_review",
                Stage: "cross_stage",
                Level: HiringDiagnosticPriority.Required,
                Category: "config_governance",
                Question: "业务阶段已经完成，但仍有需要复核的 Handoff 项。",
                Evidence: $"待复核 Handoff: {string.Join(", ", needsReviewHandoffIds)}",
                SuggestedAction: "先完成受影响 Handoff 的复核，再进入打包。",
                RelatedHandoffIds: needsReviewHandoffIds));
        }

        var businessStagesReady = stageReadiness.All(item =>
            string.Equals(item.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase));
        var readyForPackaging = businessStagesReady && needsReviewHandoffIds.Length == 0;
        var currentStage = ResolveCurrentStage(stageReadiness, businessStagesReady, readyForPackaging);
        var status = readyForPackaging
            ? HiringDiagnosticStatus.Pass
            : businessStagesReady
                ? HiringDiagnosticStatus.Warning
                : HiringDiagnosticStatus.Blocked;

        return new HiringDiagnosticReportDto(
            Status: status,
            Confidence: "high",
            CurrentStage: currentStage,
            ReadyForPackaging: readyForPackaging,
            StageReadiness: stageReadiness,
            DiagnosticTodos: diagnosticTodos,
            HandoffCorrelation: handoffItems.Select(item => item.HandoffId).ToArray(),
            OpenQuestions: [],
            UserSummary: BuildDiagnosticSummary(currentStage, readyForPackaging),
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    public static HiringDiagnosticReportDto MergeDiagnosticReports(
        HiringDiagnosticReportDto evaluated,
        HiringDiagnosticReportDto? aiReport)
    {
        if (aiReport is null)
        {
            return evaluated;
        }

        return evaluated with
        {
            Confidence = string.IsNullOrWhiteSpace(aiReport.Confidence)
                ? evaluated.Confidence
                : aiReport.Confidence,
            DiagnosticTodos = aiReport.DiagnosticTodos.Count > 0
                ? aiReport.DiagnosticTodos
                : evaluated.DiagnosticTodos,
            HandoffCorrelation = aiReport.HandoffCorrelation.Count > 0
                ? aiReport.HandoffCorrelation
                : evaluated.HandoffCorrelation,
            OpenQuestions = aiReport.OpenQuestions.Count > 0
                ? aiReport.OpenQuestions
                : evaluated.OpenQuestions,
            UserSummary = string.IsNullOrWhiteSpace(aiReport.UserSummary)
                ? evaluated.UserSummary
                : aiReport.UserSummary,
            GeneratedAtUtc = aiReport.GeneratedAtUtc > evaluated.GeneratedAtUtc
                ? aiReport.GeneratedAtUtc
                : evaluated.GeneratedAtUtc
        };
    }

    public static IReadOnlyList<HiringStageCompletionDto> BuildStageCompletion(
        IReadOnlyList<DiscoveryStageRule> stageRules,
        HiringDiagnosticReportDto? diagnosticReport)
    {
        return stageRules
            .Select(rule =>
            {
                var isReady = string.Equals(rule.Stage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase)
                    ? diagnosticReport?.ReadyForPackaging == true
                    : diagnosticReport?.StageReadiness.FirstOrDefault(item =>
                        string.Equals(item.Stage, rule.Stage, StringComparison.OrdinalIgnoreCase)) is { } readiness &&
                      (string.Equals(readiness.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(readiness.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase));

                return new HiringStageCompletionDto(
                    Stage: rule.Stage,
                    RequiredFieldCount: rule.RequiredFields.Count,
                    SatisfiedFieldCount: isReady ? rule.RequiredFields.Count : 0,
                    CompletionRate: isReady ? 1m : 0m,
                    SatisfiedFields: isReady ? rule.RequiredFields : [],
                    BlockingFields: isReady ? [] : rule.RequiredFields,
                    ReadyForNextStage: isReady);
            })
            .ToArray();
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

    private static HiringStageReadinessDto BuildMaterialStageReadiness(HiringRuntimeContext runtimeContext)
    {
        var materialHandoffs = GetActiveStageHandoffs(runtimeContext.HandoffItems, HiringCollectionStage.Material);
        if (materialHandoffs.Length == 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Material,
                HiringStageReadinessStatus.Missing,
                "资料阶段尚未形成任何 material Handoff。",
                []);
        }

        var invalidPayloadHandoffIds = materialHandoffs
            .Where(item => !HasNonEmptyString(item.Payload, "objective") && !HasNonEmptyStringArray(item.Payload, "source_files"))
            .Select(item => item.HandoffId)
            .ToArray();
        if (invalidPayloadHandoffIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Material,
                HiringStageReadinessStatus.Partial,
                "仍有 material Handoff 未达到可验证的资料目标定义。",
                invalidPayloadHandoffIds);
        }

        var blockingHandoffIds = GetUnconfirmedHandoffIds(materialHandoffs);
        if (blockingHandoffIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Material,
                HiringStageReadinessStatus.Partial,
                "仍有 material Handoff 未完成确认闭环。",
                blockingHandoffIds);
        }

        return new HiringStageReadinessDto(
            HiringCollectionStage.Material,
            HiringStageReadinessStatus.Complete,
            "material Handoff 已全部确认。",
            []);
    }

    private static HiringStageReadinessDto BuildSkillStageReadiness(HiringRuntimeContext runtimeContext)
    {
        var skillHandoffs = GetActiveStageHandoffs(runtimeContext.HandoffItems, HiringCollectionStage.Skill);
        if (skillHandoffs.Length == 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Skill,
                HiringStageReadinessStatus.Missing,
                "技能阶段尚未形成基线确认或补充 skill Handoff。",
                []);
        }

        var invalidPayloadHandoffIds = skillHandoffs
            .Where(item => !HasNonEmptyArray(item.Payload, "skills"))
            .Select(item => item.HandoffId)
            .ToArray();
        if (invalidPayloadHandoffIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Skill,
                HiringStageReadinessStatus.Partial,
                "仍有 skill Handoff 缺少完整的 payload.skills 定义。",
                invalidPayloadHandoffIds);
        }

        var blockingHandoffIds = GetUnconfirmedHandoffIds(skillHandoffs);
        if (blockingHandoffIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Skill,
                HiringStageReadinessStatus.Partial,
                "仍有 skill Handoff 未完成确认闭环。",
                blockingHandoffIds);
        }

        return new HiringStageReadinessDto(
            HiringCollectionStage.Skill,
            HiringStageReadinessStatus.Complete,
            "skill Handoff 已全部确认。",
            []);
    }

    private static HiringStageReadinessDto BuildExternalStageReadiness(HiringRuntimeContext runtimeContext)
    {
        var externalHandoffs = GetActiveStageHandoffs(runtimeContext.HandoffItems, HiringCollectionStage.External);
        if (externalHandoffs.Length == 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Missing,
                "外部阶段尚未形成任何 external Handoff。",
                []);
        }

        var capabilityGroups = new List<ExternalCapabilityGroup>(externalHandoffs.Length);
        var invalidPayloadHandoffIds = new List<string>();
        foreach (var handoff in externalHandoffs)
        {
            if (!TryParseExternalCapabilities(handoff, out var capabilities))
            {
                invalidPayloadHandoffIds.Add(handoff.HandoffId);
                continue;
            }

            capabilityGroups.Add(new ExternalCapabilityGroup(handoff, capabilities));
        }

        if (invalidPayloadHandoffIds.Count > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "仍有 external Handoff 缺少完整的 external_capabilities 定义。",
                invalidPayloadHandoffIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        var blockingHandoffIds = GetUnconfirmedHandoffIds(externalHandoffs);
        if (blockingHandoffIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "仍有 external Handoff 未完成确认闭环。",
                blockingHandoffIds);
        }

        if (capabilityGroups.All(group => group.Capabilities.All(capability => capability.IsSkip)))
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Skipped,
                "外部阶段已通过 skip Handoff 明确跳过。",
                []);
        }

        var pendingCredentialHandoffIds = ResolvePendingExternalCredentialHandoffIds(
            runtimeContext.CredentialSlots,
            capabilityGroups);
        if (pendingCredentialHandoffIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "仍有 external Handoff 的凭据槽位未完成绑定。",
                pendingCredentialHandoffIds);
        }

        return new HiringStageReadinessDto(
            HiringCollectionStage.External,
            HiringStageReadinessStatus.Complete,
            "external Handoff 已全部确认，且凭据绑定已就绪。",
            []);
    }

    private static HiringWorkflowHandoffDto[] GetActiveStageHandoffs(
        IReadOnlyList<HiringWorkflowHandoffDto> handoffItems,
        string stage)
    {
        return handoffItems
            .Where(item =>
                string.Equals(item.Stage, stage, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Status, HiringHandoffStatus.Dismissed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.HandoffId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] GetUnconfirmedHandoffIds(IReadOnlyList<HiringWorkflowHandoffDto> handoffItems)
    {
        return handoffItems
            .Where(item => !string.Equals(item.Status, HiringHandoffStatus.Confirmed, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.HandoffId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolvePendingExternalCredentialHandoffIds(
        IReadOnlyList<HiringCredentialSlotDto> credentialSlots,
        IReadOnlyList<ExternalCapabilityGroup> capabilityGroups)
    {
        var pendingHandoffIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in capabilityGroups)
        {
            foreach (var capability in group.Capabilities)
            {
                if (capability.IsSkip ||
                    string.Equals(capability.AuthKind, "none", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(capability.CredentialSlot))
                {
                    pendingHandoffIds.Add(group.Handoff.HandoffId);
                    continue;
                }

                var slot = credentialSlots.FirstOrDefault(candidate =>
                    string.Equals(candidate.CredentialSlot, capability.CredentialSlot, StringComparison.OrdinalIgnoreCase));
                if (slot is null)
                {
                    pendingHandoffIds.Add(group.Handoff.HandoffId);
                    continue;
                }

                if (!string.Equals(slot.BindingStatus, HiringCredentialBindingStatus.Bound, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(slot.BindingStatus, HiringCredentialBindingStatus.NotRequired, StringComparison.OrdinalIgnoreCase))
                {
                    pendingHandoffIds.Add(string.IsNullOrWhiteSpace(slot.HandoffId) ? group.Handoff.HandoffId : slot.HandoffId);
                }
            }
        }

        return pendingHandoffIds.ToArray();
    }

    private static bool TryParseExternalCapabilities(
        HiringWorkflowHandoffDto handoff,
        out IReadOnlyList<ExternalCapability> capabilities)
    {
        capabilities = [];
        if (handoff.Payload.ValueKind != JsonValueKind.Object ||
            !TryReadPayloadArray(handoff.Payload, "external_capabilities", out var externalCapabilities) ||
            externalCapabilities.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<ExternalCapability>();
        foreach (var item in externalCapabilities.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var kind = TryReadPayloadString(item, out var capabilityKind, "kind")
                ? capabilityKind
                : "normal";
            if (string.Equals(kind, "skip", StringComparison.OrdinalIgnoreCase))
            {
                parsed.Add(new ExternalCapability(true, null, null, null, null, "none", []));
                continue;
            }

            if (!TryReadPayloadString(item, out var category, "category") ||
                !TryReadPayloadString(item, out var objective, "objective") ||
                !TryReadPayloadString(item, out var targetSystem, "target_system", "targetSystem") ||
                !TryReadPayloadString(item, out var authKind, "auth_kind", "authKind") ||
                !TryReadPayloadStringArray(item, out var linkedSkills, "linked_skills", "linkedSkills") ||
                linkedSkills.Length == 0)
            {
                return false;
            }

            TryReadPayloadString(item, out var credentialSlot, "credential_slot", "credentialSlot");
            parsed.Add(new ExternalCapability(
                false,
                category,
                objective,
                targetSystem,
                string.IsNullOrWhiteSpace(credentialSlot) ? null : credentialSlot,
                authKind,
                linkedSkills));
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        capabilities = parsed;
        return true;
    }

    private static bool HasNonEmptyArray(JsonElement payload, string propertyName)
    {
        return TryReadPayloadArray(payload, propertyName, out var arrayElement) &&
               arrayElement.ValueKind == JsonValueKind.Array &&
               arrayElement.GetArrayLength() > 0;
    }

    private static bool HasNonEmptyString(JsonElement payload, params string[] propertyNames)
    {
        return TryReadPayloadString(payload, out _, propertyNames);
    }

    private static bool HasNonEmptyStringArray(JsonElement payload, params string[] propertyNames)
    {
        return TryReadPayloadStringArray(payload, out var values, propertyNames) && values.Length > 0;
    }

    private static bool TryReadPayloadString(JsonElement payload, out string value, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    value = string.Empty;
                    return false;
                }

                value = property.Value.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadPayloadStringArray(
        JsonElement payload,
        out string[] values,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var singleValue = property.Value.GetString()?.Trim();
                    values = string.IsNullOrWhiteSpace(singleValue) ? [] : [singleValue];
                    return values.Length > 0;
                }

                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    values = [];
                    return false;
                }

                values = property.Value
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()?.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return values.Length > 0;
            }
        }

        values = [];
        return false;
    }

    private static bool TryReadPayloadArray(JsonElement payload, string propertyName, out JsonElement value)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string ResolveCurrentStage(
        IReadOnlyList<HiringStageReadinessDto> stageReadiness,
        bool businessStagesReady,
        bool readyForPackaging)
    {
        if (readyForPackaging || businessStagesReady)
        {
            return HiringCollectionStage.ReadyForPackaging;
        }

        return stageReadiness.FirstOrDefault(item =>
                !string.Equals(item.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase))
            ?.Stage ?? HiringCollectionStage.Material;
    }

    private static string BuildDiagnosticQuestion(string stage)
    {
        return stage switch
        {
            var value when string.Equals(value, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase)
                => "资料 Handoff 是否已经全部闭环确认？",
            var value when string.Equals(value, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)
                => "技能基线确认单和补充 skill Handoff 是否已经全部确认？",
            var value when string.Equals(value, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase)
                => "external Handoff 是否全部确认，且需要的凭据槽位已经绑定？",
            _ => "仍有阶段未完成。"
        };
    }

    private static string BuildDiagnosticAction(string stage)
    {
        return stage switch
        {
            var value when string.Equals(value, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase)
                => "继续补齐或确认 material Handoff。",
            var value when string.Equals(value, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)
                => "继续补齐 skill Handoff，或完成技能基线确认。",
            var value when string.Equals(value, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase)
                => "继续补齐 external Handoff，并完成凭据绑定或 skip 确认。",
            _ => "继续处理当前阻塞项。"
        };
    }

    private static string BuildDiagnosticSummary(string currentStage, bool readyForPackaging)
    {
        if (readyForPackaging)
        {
            return "资料、技能和外部能力的 Handoff 都已闭环，可以进入打包。";
        }

        if (string.Equals(currentStage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase))
        {
            return "业务阶段已经完成，但仍有复核项需要处理。";
        }

        return $"当前还需要补齐 {DisplayStage(currentStage)} 阶段。";
    }

    private static string DisplayStage(string stage)
    {
        return stage switch
        {
            var value when string.Equals(value, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase) => "资料",
            var value when string.Equals(value, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase) => "技能",
            var value when string.Equals(value, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase) => "外部",
            var value when string.Equals(value, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase) => "打包准备",
            _ => stage
        };
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

internal sealed record ExternalCapabilityGroup(
    HiringWorkflowHandoffDto Handoff,
    IReadOnlyList<ExternalCapability> Capabilities);

internal sealed record ExternalCapability(
    bool IsSkip,
    string? Category,
    string? Objective,
    string? TargetSystem,
    string? CredentialSlot,
    string AuthKind,
    IReadOnlyList<string> LinkedSkills);
