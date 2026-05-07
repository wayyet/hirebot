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
            var tagContent = match.Groups["content"].Value.Trim();
            if (string.IsNullOrWhiteSpace(tagContent))
            {
                continue;
            }

            switch (tagName)
            {
                case "dispatch":
                    dispatchCommands.Add(DeserializeRequired<HiringDispatchCommand>(tagContent));
                    break;
                case "dispatch_callback":
                    dispatchCallbacks.Add(DeserializeRequired<HiringDispatchCallbackPayload>(tagContent));
                    break;
                case "diagnostic_report":
                    diagnosticReport = DeserializeRequired<HiringDiagnosticReportDto>(tagContent);
                    break;
                case "config_governance_patch":
                    configFiles.AddRange(DeserializeRequired<HiringConfigGovernancePatchDocument>(tagContent).Files);
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
        var workflowTodos = runtimeContext.WorkflowTodos;
        var materialReadiness = BuildStageReadiness(
            HiringCollectionStage.Material,
            workflowTodos.Where(todo => string.Equals(todo.Stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase)).ToArray());
        var skillReadiness = BuildStageReadiness(
            HiringCollectionStage.Skill,
            workflowTodos.Where(todo => string.Equals(todo.Stage, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)).ToArray());
        var externalReadiness = BuildExternalStageReadiness(runtimeContext);

        var stageReadiness = new[]
        {
            materialReadiness,
            skillReadiness,
            externalReadiness
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
                Level: HiringTodoPriority.Required,
                Category: "stage_readiness",
                Question: BuildDiagnosticQuestion(readiness.Stage),
                Evidence: readiness.Reason,
                SuggestedAction: $"继续补齐 {DisplayStage(readiness.Stage)} 阶段，并完成关联 required todo。",
                RelatedTodoIds: readiness.BlockingTodoIds));
        }

        var needsReviewTodoIds = workflowTodos
            .Where(todo => string.Equals(todo.Kind, HiringTodoKind.Gap, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(todo.Status, HiringTodoStatus.NeedsReview, StringComparison.OrdinalIgnoreCase))
            .Select(todo => todo.Id)
            .Concat(runtimeContext.ConfigGovernance?.PendingReviewTodoIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (needsReviewTodoIds.Length > 0)
        {
            diagnosticTodos.Add(new HiringDiagnosticTodoDto(
                Id: "d_cross_stage_needs_review",
                Stage: "cross_stage",
                Level: HiringTodoPriority.Required,
                Category: "config_governance",
                Question: "已完成的缺口可能受配置文件变更影响，是否需要重新复核？",
                Evidence: $"待复核 todo: {string.Join(", ", needsReviewTodoIds)}",
                SuggestedAction: "检查配置治理变更影响，并重新确认或重新分发受影响的 todo。",
                RelatedTodoIds: needsReviewTodoIds));
        }

        var readyForPackaging = stageReadiness.All(item =>
                string.Equals(item.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase)) &&
            needsReviewTodoIds.Length == 0;
        var status = readyForPackaging
            ? HiringDiagnosticStatus.Pass
            : HiringDiagnosticStatus.Blocked;
        var currentStage = ResolveCurrentStage(stageReadiness, readyForPackaging);
        var userSummary = readyForPackaging
            ? "资料、技能和外部阶段都已满足要求，可以进入打包。"
            : $"当前仍需补齐 {DisplayStage(currentStage)} 阶段后才能继续。";

        return new HiringDiagnosticReportDto(
            Status: status,
            Confidence: "high",
            CurrentStage: currentStage,
            ReadyForPackaging: readyForPackaging,
            StageReadiness: stageReadiness,
            DiagnosticTodos: diagnosticTodos,
            TodoCorrelation: workflowTodos
                .Where(todo => string.Equals(todo.Kind, HiringTodoKind.Gap, StringComparison.OrdinalIgnoreCase))
                .Select(todo => todo.Id)
                .ToArray(),
            OpenQuestions: [],
            UserSummary: userSummary,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    private static readonly Dictionary<string, int> StageOrderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [HiringCollectionStage.Material] = 0,
        [HiringCollectionStage.Skill] = 1,
        [HiringCollectionStage.External] = 2,
        [HiringCollectionStage.ReadyForPackaging] = 3
    };

    public static HiringDiagnosticReportDto MergeDiagnosticReports(
        HiringDiagnosticReportDto evaluated,
        HiringDiagnosticReportDto? aiReport)
    {
        if (aiReport is null)
        {
            return evaluated;
        }

        var effectiveCurrentStage = ResolveEffectiveCurrentStage(
            evaluated.CurrentStage,
            aiReport.CurrentStage,
            aiReport.StageReadiness);

        var mergedStageReadiness = MergeStageReadiness(
            evaluated.StageReadiness,
            aiReport.StageReadiness);

        var readyForPackaging = string.Equals(
            effectiveCurrentStage,
            HiringCollectionStage.ReadyForPackaging,
            StringComparison.OrdinalIgnoreCase);

        return evaluated with
        {
            CurrentStage = effectiveCurrentStage,
            StageReadiness = mergedStageReadiness,
            ReadyForPackaging = readyForPackaging,
            Confidence = string.IsNullOrWhiteSpace(aiReport.Confidence)
                ? evaluated.Confidence
                : aiReport.Confidence,
            DiagnosticTodos = aiReport.DiagnosticTodos.Count > 0
                ? aiReport.DiagnosticTodos
                : evaluated.DiagnosticTodos,
            TodoCorrelation = aiReport.TodoCorrelation.Count > 0
                ? aiReport.TodoCorrelation
                : evaluated.TodoCorrelation,
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

    private static string ResolveEffectiveCurrentStage(
        string evaluatedStage,
        string aiStage,
        IReadOnlyList<HiringStageReadinessDto> aiStageReadiness)
    {
        if (string.Equals(aiStage, evaluatedStage, StringComparison.OrdinalIgnoreCase))
        {
            return evaluatedStage;
        }

        if (!IsStageAhead(aiStage, evaluatedStage))
        {
            return evaluatedStage;
        }

        var aiDeclaresPriorStagesComplete = StageOrderMap
            .Where(kv => kv.Value < GetStageOrder(aiStage))
            .All(kv =>
            {
                var aiReadiness = aiStageReadiness.FirstOrDefault(
                    item => string.Equals(item.Stage, kv.Key, StringComparison.OrdinalIgnoreCase));
                return aiReadiness is not null &&
                       (string.Equals(aiReadiness.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(aiReadiness.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase));
            });

        return aiDeclaresPriorStagesComplete ? aiStage : evaluatedStage;
    }

    private static bool IsStageAhead(string candidate, string baseline)
    {
        var candidateOrder = GetStageOrder(candidate);
        var baselineOrder = GetStageOrder(baseline);
        return candidateOrder > baselineOrder;
    }

    private static int GetStageOrder(string stage)
    {
        return StageOrderMap.TryGetValue(stage, out var order) ? order : -1;
    }

    private static IReadOnlyList<HiringStageReadinessDto> MergeStageReadiness(
        IReadOnlyList<HiringStageReadinessDto> evaluated,
        IReadOnlyList<HiringStageReadinessDto> aiReport)
    {
        return evaluated
            .Select(evaluatedItem =>
            {
                var aiItem = aiReport.FirstOrDefault(
                    item => string.Equals(item.Stage, evaluatedItem.Stage, StringComparison.OrdinalIgnoreCase));
                if (aiItem is not null &&
                    (string.Equals(aiItem.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(aiItem.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase)))
                {
                    return aiItem;
                }

                return evaluatedItem;
            })
            .ToArray();
    }

    public static IReadOnlyList<HiringStageCompletionDto> BuildStageCompletion(
        IReadOnlyList<DiscoveryStageRule> stageRules,
        HiringDiagnosticReportDto? diagnosticReport)
    {
        return stageRules
            .Select(rule =>
            {
                var readiness = diagnosticReport?.StageReadiness.FirstOrDefault(item =>
                    string.Equals(item.Stage, rule.Stage, StringComparison.OrdinalIgnoreCase));
                var isReady = readiness is not null &&
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

    private static HiringStageReadinessDto BuildStageReadiness(string stage, IReadOnlyList<HiringWorkflowTodoDto> todos)
    {
        var requiredGapTodos = todos
            .Where(IsRequiredGapTodo)
            .ToArray();
        if (requiredGapTodos.Length == 0)
        {
            return new HiringStageReadinessDto(
                stage,
                HiringStageReadinessStatus.Missing,
                $"{DisplayStage(stage)}阶段缺少 required gap todo。",
                []);
        }

        var activeRequiredTodos = requiredGapTodos
            .Where(todo => !IsTodoDismissed(todo))
            .ToArray();
        if (activeRequiredTodos.Length == 0)
        {
            return new HiringStageReadinessDto(
                stage,
                HiringStageReadinessStatus.Missing,
                $"{DisplayStage(stage)}阶段的 required gap todo 均被忽略，当前仍不满足推进条件。",
                []);
        }

        var blockingTodoIds = activeRequiredTodos
            .Where(todo => !IsTodoDone(todo))
            .Select(todo => todo.Id)
            .ToArray();
        if (blockingTodoIds.Length == 0)
        {
            return new HiringStageReadinessDto(
                stage,
                HiringStageReadinessStatus.Complete,
                $"{DisplayStage(stage)}阶段的 required gap todo 已全部完成。",
                activeRequiredTodos.Select(todo => todo.Id).ToArray());
        }

        return new HiringStageReadinessDto(
            stage,
            HiringStageReadinessStatus.Partial,
            $"{DisplayStage(stage)}阶段仍有 required gap todo 未完成。",
            blockingTodoIds);
    }

    private static HiringStageReadinessDto BuildExternalStageReadiness(HiringRuntimeContext runtimeContext)
    {
        var externalTodos = runtimeContext.WorkflowTodos
            .Where(todo => string.Equals(todo.Stage, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var skipTodo = externalTodos.FirstOrDefault(todo =>
            string.Equals(todo.Kind, HiringTodoKind.Gap, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(todo.GapType, "external_skip_declaration", StringComparison.OrdinalIgnoreCase) &&
            IsTodoDone(todo));
        if (skipTodo is not null)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Skipped,
                "用户已明确跳过外部阶段。",
                [skipTodo.Id]);
        }

        var readiness = BuildStageReadiness(HiringCollectionStage.External, externalTodos);
        if (!string.Equals(readiness.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase))
        {
            return readiness;
        }

        var pendingCredentialTodoIds = runtimeContext.CredentialSlots
            .Where(slot => !string.Equals(slot.BindingStatus, HiringCredentialBindingStatus.Bound, StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(slot.BindingStatus, HiringCredentialBindingStatus.NotRequired, StringComparison.OrdinalIgnoreCase))
            .Select(slot => slot.TodoId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        if (pendingCredentialTodoIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "外部能力配置已产出，但仍有凭据槽位待绑定。",
                pendingCredentialTodoIds);
        }

        return readiness;
    }

    private static bool IsRequiredGapTodo(HiringWorkflowTodoDto todo)
    {
        return string.Equals(todo.Kind, HiringTodoKind.Gap, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(todo.Priority, HiringTodoPriority.Required, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTodoDismissed(HiringWorkflowTodoDto todo)
    {
        return string.Equals(todo.Status, HiringTodoStatus.Dismissed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTodoDone(HiringWorkflowTodoDto todo)
    {
        return string.Equals(todo.Status, HiringTodoStatus.Done, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(todo.Status, HiringTodoStatus.Resolved, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveCurrentStage(IReadOnlyList<HiringStageReadinessDto> stageReadiness, bool readyForPackaging)
    {
        if (readyForPackaging)
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
                => "还缺至少一条可完成的资料阶段 required gap todo。",
            var value when string.Equals(value, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)
                => "还缺至少一条可完成的技能阶段 required gap todo。",
            var value when string.Equals(value, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase)
                => "还缺外部阶段 required gap todo 的完成结果，或明确的跳过声明。",
            _ => "仍有阶段未完成。"
        };
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

    private static T DeserializeRequired<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                   ?? throw new InvalidOperationException($"无法解析 {typeof(T).Name} 结构化事件。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"结构化事件 JSON 无法解析为 {typeof(T).Name}。", ex);
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
    IReadOnlyList<string> TodoIds,
    string? Note,
    string? Mode);

internal sealed record HiringDispatchCallbackArtifactPayload(
    string Path,
    string Kind,
    string Encoding,
    string? Content,
    string Sha256);

internal sealed record HiringDispatchCallbackTodoResultPayload(
    string TodoId,
    string Status,
    IReadOnlyList<HiringDispatchCallbackArtifactPayload> Artifacts,
    IReadOnlyList<HiringCredentialSlotDto>? CredentialSlots,
    IReadOnlyList<string> Errors);

internal sealed record HiringDispatchCallbackPayload(
    string SourceDispatchTarget,
    IReadOnlyList<string> TodoIds,
    string UserSummary,
    IReadOnlyList<HiringDispatchCallbackArtifactPayload> Artifacts,
    IReadOnlyList<HiringDispatchCallbackTodoResultPayload> TodoResults,
    string Status,
    IReadOnlyList<string> Errors);

internal sealed record HiringConfigGovernancePatchDocument(
    IReadOnlyList<HiringConfigGovernanceFileDto> Files);
