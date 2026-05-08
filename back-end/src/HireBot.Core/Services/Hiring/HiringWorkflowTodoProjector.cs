using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;

namespace HireBot.Core.Services.Hiring;

internal static class HiringWorkflowTodoProjector
{
    private static readonly string[] BusinessStageOrder =
    [
        HiringCollectionStage.Material,
        HiringCollectionStage.Skill,
        HiringCollectionStage.External
    ];

    public static HiringWorkflowTodoProjectionResult ProjectAuthoritativeTodos(
        IReadOnlyList<SandboxSessionTodoItemDto> todoItems)
    {
        if (todoItems.Count == 0)
        {
            return new HiringWorkflowTodoProjectionResult([], []);
        }

        var projectedTodos = new List<HiringWorkflowTodoDto>(todoItems.Count);
        var warnings = new List<HiringWorkflowTodoProjectionWarning>();

        foreach (var todoItem in todoItems)
        {
            if (TryProjectTodoItem(todoItem, out var projectedTodo, out var warning))
            {
                projectedTodos.Add(projectedTodo!);
                continue;
            }

            if (warning is not null)
            {
                warnings.Add(warning);
            }
        }

        return new HiringWorkflowTodoProjectionResult(
            projectedTodos
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            warnings);
    }

    public static IReadOnlyList<HiringWorkflowTodoDto> BuildDisplayTodos(HiringRuntimeContext runtimeContext)
    {
        var authoritativeTodos = runtimeContext.WorkflowTodos
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fallbackTodos = BuildFallbackTodos(runtimeContext, authoritativeTodos);
        if (fallbackTodos.Count == 0)
        {
            return authoritativeTodos;
        }

        return authoritativeTodos
            .Concat(fallbackTodos)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<HiringWorkflowTodoDto> BuildFallbackTodos(
        HiringRuntimeContext runtimeContext,
        IReadOnlyList<HiringWorkflowTodoDto> authoritativeTodos)
    {
        // Only generate fallback todos for the current active (first non-complete) stage.
        // Do NOT pre-generate fallbacks for future stages — they will appear when
        // the stage becomes active and the sandbox still hasn't provided structured data.
        var activeStage = BusinessStageOrder.FirstOrDefault(stage =>
        {
            var readiness = runtimeContext.StageReadiness.FirstOrDefault(item =>
                string.Equals(item.Stage, stage, StringComparison.OrdinalIgnoreCase));
            return readiness is not null && !IsReadinessSatisfied(readiness.Status);
        });

        if (activeStage is null)
        {
            return [];
        }

        if (authoritativeTodos.Any(todo => string.Equals(todo.Stage, activeStage, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        var readiness = runtimeContext.StageReadiness.First(item =>
            string.Equals(item.Stage, activeStage, StringComparison.OrdinalIgnoreCase));
        var stageRule = runtimeContext.DiscoverySkill?.StageRules.FirstOrDefault(rule =>
            string.Equals(rule.Stage, activeStage, StringComparison.OrdinalIgnoreCase));
        var requiredFields = stageRule?.RequiredFields ?? [];
        if (requiredFields.Count == 0)
        {
            requiredFields = GetDefaultRequiredFields(activeStage);
        }

        var now = DateTimeOffset.UtcNow;
        var results = new List<HiringWorkflowTodoDto>(requiredFields.Count);
        var index = 0;
        foreach (var field in requiredFields)
        {
            ++index;
            var (title, currentState, expectedState, acceptanceCriteria, gapType) =
                BuildFallbackFields(activeStage, field, index, readiness.Reason);

            results.Add(new HiringWorkflowTodoDto(
                Id: $"fallback::{runtimeContext.HireId}::{activeStage}::{index}",
                Title: title,
                Stage: activeStage,
                Kind: HiringTodoKind.Gap,
                Status: HiringTodoStatus.InProgress,
                GapType: gapType,
                Priority: HiringTodoPriority.Required,
                CurrentState: currentState,
                ExpectedState: expectedState,
                AcceptanceCriteria: acceptanceCriteria,
                AcceptanceEvidence: null,
                Source: $"system:fallback:{activeStage}",
                Fingerprint: $"fallback::{activeStage}::{index}",
                Category: activeStage,
                Payload: null,
                Level: null,
                Question: null,
                Evidence: null,
                SuggestedAction: null,
                RelatedTodoIds: [],
                RelatedFiles: [],
                CreatedAtUtc: now,
                UpdatedAtUtc: now));
        }

        return results;
    }

    private static IReadOnlyList<string> GetDefaultRequiredFields(string stage)
    {
        return stage switch
        {
            HiringCollectionStage.Material => ["业务资料补齐"],
            HiringCollectionStage.Skill =>
            [
                "技能 A - 业务规则抽取",
                "技能 B - 对话流程管理",
                "技能 C - 异常与兜底处理"
            ],
            HiringCollectionStage.External =>
            [
                "外部系统 A - MCP 工具代理",
                "外部系统 B - CLI 命令通道",
                "外部系统 C - 数据库连接"
            ],
            _ => []
        };
    }

    private static (string title, string currentState, string expectedState, string acceptanceCriteria, string gapType)
        BuildFallbackFields(string stage, string field, int index, string readinessReason)
    {
        return stage switch
        {
            HiringCollectionStage.Material => (
                "补齐资料分类与抽取目标",
                readinessReason,
                "至少上传 1 份业务资料，并完成分类且为每份资料写明抽取目标。",
                "资料已上传、完成分类，并为每份资料写明抽取目标。",
                $"fallback_{stage}_readiness"
            ),
            HiringCollectionStage.Skill => (
                field,
                $"待创建技能「{field}」。",
                $"定义「{field}」的触发条件、输入输出边界与验收标准。",
                $"「{field}」的技能描述已明确，可进入后续创建流程。",
                $"fallback_skill_{index}"
            ),
            HiringCollectionStage.External => (
                field,
                $"待对接系统「{field}」。",
                $"明确「{field}」的目标系统、操作类型、认证方式与 connector payload 结构。",
                $"「{field}」的连接能力、凭据槽位和 payload 结构已明确。",
                $"fallback_external_{index}"
            ),
            _ => (
                "补齐当前阶段信息",
                readinessReason,
                readinessReason,
                readinessReason,
                $"fallback_{stage}_readiness"
            )
        };
    }

    private static bool TryProjectTodoItem(
        SandboxSessionTodoItemDto todoItem,
        out HiringWorkflowTodoDto? projectedTodo,
        out HiringWorkflowTodoProjectionWarning? warning)
    {
        projectedTodo = null;
        warning = null;

        if (string.IsNullOrWhiteSpace(todoItem.Notes))
        {
            warning = new HiringWorkflowTodoProjectionWarning(todoItem.Id, "Todo 缺少 notes JSON。");
            return false;
        }

        try
        {
            projectedTodo = ProjectTodoItemStrict(todoItem);
            return true;
        }
        catch (JsonException ex)
        {
            warning = new HiringWorkflowTodoProjectionWarning(todoItem.Id, $"Todo notes JSON 无法解析: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            warning = new HiringWorkflowTodoProjectionWarning(todoItem.Id, ex.Message);
            return false;
        }
    }

    private static HiringWorkflowTodoDto ProjectTodoItemStrict(SandboxSessionTodoItemDto todoItem)
    {
        var notes = ParseWorkflowTodoNotes(todoItem.Id, todoItem.Notes!);
        var todoId = RequireTodoField(todoItem.Id, nameof(todoItem.Id), todoItem.Id);
        var title = RequireTodoField(todoId, nameof(todoItem.Text), todoItem.Text);
        var stage = NormalizeRequiredTodoStage(todoId, notes.Stage);
        var kind = NormalizeRequiredTodoKind(todoId, notes.Kind);
        var status = NormalizeRequiredTodoStatus(todoId, RequireTodoField(todoId, nameof(WorkflowTodoNotes.Status), notes.Status));
        var source = RequireTodoField(todoId, nameof(WorkflowTodoNotes.Source), notes.Source);
        var createdAtUtc = notes.CreatedAtUtc ?? todoItem.CreatedAtUtc;
        var updatedAtUtc = notes.UpdatedAtUtc ?? notes.CreatedAtUtc ?? todoItem.UpdatedAtUtc;

        if (string.Equals(kind, HiringTodoKind.Gap, StringComparison.OrdinalIgnoreCase))
        {
            return new HiringWorkflowTodoDto(
                Id: todoId,
                Title: title,
                Stage: stage,
                Kind: kind,
                Status: status,
                GapType: RequireTodoField(todoId, nameof(WorkflowTodoNotes.GapType), notes.GapType),
                Priority: NormalizeRequiredTodoPriority(todoId, notes.Priority),
                CurrentState: RequireTodoField(todoId, nameof(WorkflowTodoNotes.CurrentState), notes.CurrentState),
                ExpectedState: RequireTodoField(todoId, nameof(WorkflowTodoNotes.ExpectedState), notes.ExpectedState),
                AcceptanceCriteria: RequireTodoField(todoId, nameof(WorkflowTodoNotes.AcceptanceCriteria), notes.AcceptanceCriteria),
                AcceptanceEvidence: TrimOrNull(notes.AcceptanceEvidence),
                Source: source,
                Fingerprint: RequireTodoField(todoId, nameof(WorkflowTodoNotes.Fingerprint), notes.Fingerprint),
                Category: TrimOrNull(notes.Category),
                Payload: notes.Payload,
                Level: null,
                Question: null,
                Evidence: null,
                SuggestedAction: null,
                RelatedTodoIds: notes.RelatedTodoIds,
                RelatedFiles: notes.RelatedFiles,
                CreatedAtUtc: createdAtUtc,
                UpdatedAtUtc: updatedAtUtc);
        }

        return new HiringWorkflowTodoDto(
            Id: todoId,
            Title: title,
            Stage: stage,
            Kind: kind,
            Status: status,
            GapType: null,
            Priority: null,
            CurrentState: null,
            ExpectedState: null,
            AcceptanceCriteria: null,
            AcceptanceEvidence: null,
            Source: source,
            Fingerprint: null,
            Category: RequireTodoField(todoId, nameof(WorkflowTodoNotes.Category), notes.Category),
            Payload: notes.Payload,
            Level: NormalizeRequiredTodoLevel(todoId, notes.Level),
            Question: RequireTodoField(todoId, nameof(WorkflowTodoNotes.Question), notes.Question),
            Evidence: RequireTodoField(todoId, nameof(WorkflowTodoNotes.Evidence), notes.Evidence),
            SuggestedAction: RequireTodoField(todoId, nameof(WorkflowTodoNotes.SuggestedAction), notes.SuggestedAction),
            RelatedTodoIds: notes.RelatedTodoIds,
            RelatedFiles: notes.RelatedFiles,
            CreatedAtUtc: createdAtUtc,
            UpdatedAtUtc: updatedAtUtc);
    }

    private static bool IsReadinessSatisfied(string? status)
    {
        return string.Equals(status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string RequireTodoField(string todoId, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Todo {todoId} 的 notes JSON 缺少必填字段 {fieldName}。");
        }

        return value.Trim();
    }

    private static WorkflowTodoNotes ParseWorkflowTodoNotes(string todoId, string rawNotesJson)
    {
        using var document = JsonDocument.Parse(rawNotesJson);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Todo {todoId} 的 notes JSON 必须是对象。");
        }

        var root = document.RootElement;
        return new WorkflowTodoNotes(
            Stage: ReadTodoString(root, "stage"),
            Kind: ReadTodoString(root, "kind"),
            GapType: ReadTodoString(root, "gap_type", "gapType"),
            Priority: ReadTodoString(root, "priority"),
            CurrentState: ReadTodoString(root, "current_state", "currentState"),
            ExpectedState: ReadTodoString(root, "expected_state", "expectedState"),
            AcceptanceCriteria: ReadTodoString(root, "acceptance_criteria", "acceptanceCriteria"),
            AcceptanceEvidence: ReadTodoString(root, "acceptance_evidence", "acceptanceEvidence"),
            Status: ReadTodoString(root, "status"),
            Source: ReadTodoString(root, "source"),
            Fingerprint: ReadTodoString(root, "fingerprint"),
            Category: ReadTodoString(root, "category"),
            Level: ReadTodoString(root, "level"),
            Question: ReadTodoString(root, "question"),
            Evidence: ReadTodoString(root, "evidence"),
            SuggestedAction: ReadTodoString(root, "suggested_action", "suggestedAction"),
            Payload: ReadTodoPayload(root),
            RelatedTodoIds: ReadTodoStringArray(root, "related_todos", "relatedTodos"),
            RelatedFiles: ReadTodoStringArray(root, "related_files", "relatedFiles"),
            CreatedAtUtc: ReadTodoTimestamp(root, "createdAtUtc", "created_at", "createdAt"),
            UpdatedAtUtc: ReadTodoTimestamp(root, "updatedAtUtc", "updated_at", "updatedAt"));
    }

    private static string? ReadTodoString(JsonElement root, params string[] propertyNames)
    {
        if (!TryGetTodoProperty(root, propertyNames, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => property.GetRawText()
        };
    }

    private static DateTimeOffset? ReadTodoTimestamp(JsonElement root, params string[] propertyNames)
    {
        if (!TryGetTodoProperty(root, propertyNames, out var property))
        {
            return null;
        }

        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(property.GetString(), out var timestamp))
        {
            return timestamp;
        }

        return null;
    }

    private static bool TryGetTodoProperty(JsonElement root, IReadOnlyList<string> propertyNames, out JsonElement property)
    {
        foreach (var candidateName in propertyNames)
        {
            foreach (var currentProperty in root.EnumerateObject())
            {
                if (string.Equals(currentProperty.Name, candidateName, StringComparison.OrdinalIgnoreCase))
                {
                    property = currentProperty.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static string NormalizeRequiredTodoStatus(string todoId, string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            HiringTodoStatus.Open => HiringTodoStatus.Open,
            HiringTodoStatus.InProgress => HiringTodoStatus.InProgress,
            HiringTodoStatus.Done => HiringTodoStatus.Done,
            HiringTodoStatus.NeedsReview => HiringTodoStatus.NeedsReview,
            HiringTodoStatus.Dismissed => HiringTodoStatus.Dismissed,
            HiringTodoStatus.Resolved => HiringTodoStatus.Resolved,
            _ => throw new InvalidOperationException($"Todo {todoId} 的 notes JSON 字段 Status 非法: {value}")
        };
    }

    private static string NormalizeRequiredTodoStage(string todoId, string? value)
    {
        return RequireTodoField(todoId, nameof(WorkflowTodoNotes.Stage), value).Trim().ToLowerInvariant() switch
        {
            "material" => HiringCollectionStage.Material,
            "skill" => HiringCollectionStage.Skill,
            "external" => HiringCollectionStage.External,
            "ready_for_packaging" => HiringCollectionStage.ReadyForPackaging,
            "cross_stage" or "cross-stage" => "cross_stage",
            _ => throw new InvalidOperationException($"Todo {todoId} 的 notes JSON 字段 stage 非法: {value}")
        };
    }

    private static string NormalizeRequiredTodoKind(string todoId, string? value)
    {
        return RequireTodoField(todoId, nameof(WorkflowTodoNotes.Kind), value).Trim().ToLowerInvariant() switch
        {
            HiringTodoKind.Gap => HiringTodoKind.Gap,
            HiringTodoKind.Diagnosis => HiringTodoKind.Diagnosis,
            _ => throw new InvalidOperationException($"Todo {todoId} 的 notes JSON 字段 kind 非法: {value}")
        };
    }

    private static string NormalizeRequiredTodoPriority(string todoId, string? value)
    {
        return NormalizeTodoTriageValue(todoId, nameof(WorkflowTodoNotes.Priority), value);
    }

    private static string NormalizeRequiredTodoLevel(string todoId, string? value)
    {
        return NormalizeTodoTriageValue(todoId, nameof(WorkflowTodoNotes.Level), value);
    }

    private static string NormalizeTodoTriageValue(string todoId, string fieldName, string? value)
    {
        return RequireTodoField(todoId, fieldName, value).Trim().ToLowerInvariant() switch
        {
            HiringTodoPriority.Required or "必需" or "必须" => HiringTodoPriority.Required,
            HiringTodoPriority.Recommended or "推荐" => HiringTodoPriority.Recommended,
            HiringTodoPriority.Optional or "可选" => HiringTodoPriority.Optional,
            _ => throw new InvalidOperationException($"Todo {todoId} 的 notes JSON 字段 {fieldName} 非法: {value}")
        };
    }

    private static JsonElement? ReadTodoPayload(JsonElement root)
    {
        if (!TryGetTodoProperty(root, ["payload"], out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.Clone();
    }

    private static IReadOnlyList<string> ReadTodoStringArray(JsonElement root, params string[] propertyNames)
    {
        if (!TryGetTodoProperty(root, propertyNames, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record WorkflowTodoNotes(
        string? Stage,
        string? Kind,
        string? GapType,
        string? Priority,
        string? CurrentState,
        string? ExpectedState,
        string? AcceptanceCriteria,
        string? AcceptanceEvidence,
        string? Category,
        string? Status,
        string? Source,
        string? Fingerprint,
        JsonElement? Payload,
        string? Level,
        string? Question,
        string? Evidence,
        string? SuggestedAction,
        IReadOnlyList<string> RelatedTodoIds,
        IReadOnlyList<string> RelatedFiles,
        DateTimeOffset? CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc);
}

internal sealed record HiringWorkflowTodoProjectionResult(
    IReadOnlyList<HiringWorkflowTodoDto> Todos,
    IReadOnlyList<HiringWorkflowTodoProjectionWarning> Warnings);

internal sealed record HiringWorkflowTodoProjectionWarning(
    string TodoId,
    string Reason);
