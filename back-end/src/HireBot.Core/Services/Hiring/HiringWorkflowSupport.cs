using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;

namespace HireBot.Core.Services.Hiring;

internal static partial class HiringWorkflowSupport
{
    private const string DemoFastTrackDefaultExtractionObjective = "演示临时默认抽取目标";

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
        HiringWorkflowStageFactsUpdate? stageFacts = null;
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
                    if (TryDeserialize<HiringDispatchCommand>(tagContent) is { } cmd)
                        dispatchCommands.Add(cmd);
                    break;
                case "dispatch_callback":
                    if (TryDeserialize<HiringDispatchCallbackPayload>(tagContent) is { } cb)
                        dispatchCallbacks.Add(cb);
                    break;
                case "diagnostic_report":
                    diagnosticReport = TryDeserialize<HiringDiagnosticReportDto>(tagContent);
                    break;
                case "config_governance_patch":
                    if (TryDeserialize<HiringConfigGovernancePatchDocument>(tagContent) is { } patch)
                        configFiles.AddRange(patch.Files);
                    break;
                case "workflow_stage_facts":
                    stageFacts = TryDeserialize<HiringWorkflowStageFactsUpdate>(tagContent);
                    break;
            }
        }

        var visibleContent = HiringTagRegex().Replace(normalizedContent, string.Empty).Trim();
        return new ParsedHiringAssistantReply(
            string.IsNullOrWhiteSpace(visibleContent) ? "已处理当前编排事件。" : visibleContent,
            dispatchCommands,
            dispatchCallbacks,
            diagnosticReport,
            configFiles,
            stageFacts);
    }

    public static HiringDiagnosticReportDto EvaluateDiagnosis(HiringRuntimeContext runtimeContext)
    {
        var workflowTodos = runtimeContext.WorkflowTodos;
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
                Level: HiringTodoPriority.Required,
                Category: "stage_readiness",
                Question: BuildDiagnosticQuestion(readiness.Stage),
                Evidence: readiness.Reason,
                SuggestedAction: BuildDiagnosticAction(readiness.Stage),
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
                Question: "业务阶段已经完成，但是否还有配置或诊断复核项未处理？",
                Evidence: $"待复核 todo: {string.Join(", ", needsReviewTodoIds)}",
                SuggestedAction: "先完成受影响工单的复核，再进入打包。",
                RelatedTodoIds: needsReviewTodoIds));
        }

        var businessStagesReady = stageReadiness.All(item =>
            string.Equals(item.Status, HiringStageReadinessStatus.Complete, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Status, HiringStageReadinessStatus.Skipped, StringComparison.OrdinalIgnoreCase));
        var readyForPackaging = businessStagesReady && needsReviewTodoIds.Length == 0;
        var currentStage = ResolveCurrentStage(stageReadiness, businessStagesReady, readyForPackaging);
        var status = readyForPackaging
            ? HiringDiagnosticStatus.Pass
            : businessStagesReady && needsReviewTodoIds.Length > 0
                ? HiringDiagnosticStatus.Warning
                : HiringDiagnosticStatus.Blocked;

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
            UserSummary: BuildDiagnosticSummary(currentStage, readyForPackaging),
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    public static HiringWorkflowRuntimeFactsDto NormalizeRuntimeFacts(HiringRuntimeContext runtimeContext)
    {
        var runtimeFacts = runtimeContext.RuntimeFacts ?? HiringWorkflowRuntimeFactsDto.Empty;
        var uploadedMaterialFiles = GetUploadedMaterialFileNames(runtimeContext.Materials);
        if (ShouldApplyDemoPackagingShortcut(uploadedMaterialFiles))
        {
            var demoMaterialExtractionTargets = uploadedMaterialFiles.ToDictionary(
                file => file,
                file => runtimeFacts.MaterialExtractionTargets.TryGetValue(file, out var objective) &&
                        !string.IsNullOrWhiteSpace(objective)
                    ? objective.Trim()
                    : DemoFastTrackDefaultExtractionObjective,
                StringComparer.OrdinalIgnoreCase);

            return new HiringWorkflowRuntimeFactsDto
            {
                MaterialReady = true,
                MaterialClassifiedFiles = uploadedMaterialFiles,
                MaterialExtractionTargets = demoMaterialExtractionTargets,
                SkillBaselineReviewed = true,
                SkillBaselineConfirmed = true
            };
        }

        var uploadedMaterialFileSet = uploadedMaterialFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var materialClassifiedFiles = runtimeFacts.MaterialClassifiedFiles
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(uploadedMaterialFileSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var materialClassifiedFileSet = materialClassifiedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var materialExtractionTargets = runtimeFacts.MaterialExtractionTargets
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) &&
                !string.IsNullOrWhiteSpace(pair.Value) &&
                uploadedMaterialFileSet.Contains(pair.Key.Trim()))
            .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
        var materialReady = uploadedMaterialFiles.Length > 0 &&
            uploadedMaterialFiles.All(materialClassifiedFileSet.Contains) &&
            uploadedMaterialFiles.All(file =>
                materialExtractionTargets.TryGetValue(file, out var objective) &&
                !string.IsNullOrWhiteSpace(objective));
        var activeSkillSupplementTodos = runtimeContext.WorkflowTodos
            .Where(todo =>
                string.Equals(todo.Stage, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase) &&
                IsRequiredGapTodo(todo) &&
                !IsTodoDismissed(todo))
            .ToArray();

        return new HiringWorkflowRuntimeFactsDto
        {
            MaterialReady = materialReady,
            MaterialClassifiedFiles = materialClassifiedFiles,
            MaterialExtractionTargets = materialExtractionTargets,
            SkillBaselineReviewed = runtimeFacts.SkillBaselineReviewed ||
                runtimeFacts.SkillBaselineConfirmed ||
                activeSkillSupplementTodos.Length > 0,
            SkillBaselineConfirmed = runtimeFacts.SkillBaselineConfirmed
        };
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
        var uploadedMaterialFiles = GetUploadedMaterialFileNames(runtimeContext.Materials);
        if (uploadedMaterialFiles.Length == 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Material,
                HiringStageReadinessStatus.Missing,
                "资料阶段至少需要上传 1 份业务资料。",
                []);
        }

        if (ShouldApplyDemoPackagingShortcut(uploadedMaterialFiles))
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Material,
                HiringStageReadinessStatus.Complete,
                "演示临时处理：检测到已上传资料，资料阶段直接视为完成。",
                []);
        }

        var runtimeFacts = runtimeContext.RuntimeFacts;
        var classifiedFileSet = runtimeFacts.MaterialClassifiedFiles
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unclassifiedFiles = uploadedMaterialFiles
            .Where(file => !classifiedFileSet.Contains(file))
            .ToArray();
        if (unclassifiedFiles.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Material,
                HiringStageReadinessStatus.Partial,
                $"仍有 {unclassifiedFiles.Length} 份资料未完成分类：{JoinPreview(unclassifiedFiles)}。",
                []);
        }

        var filesWithoutExtractionTarget = uploadedMaterialFiles
            .Where(file =>
                !runtimeFacts.MaterialExtractionTargets.TryGetValue(file, out var objective) ||
                string.IsNullOrWhiteSpace(objective))
            .ToArray();
        if (filesWithoutExtractionTarget.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Material,
                HiringStageReadinessStatus.Partial,
                $"仍有 {filesWithoutExtractionTarget.Length} 份资料未写明抽取目标：{JoinPreview(filesWithoutExtractionTarget)}。",
                []);
        }

        return new HiringStageReadinessDto(
            HiringCollectionStage.Material,
            HiringStageReadinessStatus.Complete,
            "资料已上传、分类，并为每份资料写明抽取目标。",
            []);
    }

    private static HiringStageReadinessDto BuildSkillStageReadiness(HiringRuntimeContext runtimeContext)
    {
        if (ShouldApplyDemoPackagingShortcut(GetUploadedMaterialFileNames(runtimeContext.Materials)))
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Skill,
                HiringStageReadinessStatus.Complete,
                "演示临时处理：上传资料后默认技能阶段已完备，可直接进入打包阶段。",
                []);
        }

        var runtimeFacts = runtimeContext.RuntimeFacts;
        var activeRequiredTodos = runtimeContext.WorkflowTodos
            .Where(todo =>
                string.Equals(todo.Stage, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase) &&
                IsRequiredGapTodo(todo) &&
                !IsTodoDismissed(todo))
            .ToArray();
        if (!runtimeFacts.SkillBaselineReviewed)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Skill,
                HiringStageReadinessStatus.Missing,
                "技能阶段尚未完成默认技能基线盘点。",
                []);
        }

        var blockingTodoIds = activeRequiredTodos
            .Where(todo => !IsTodoDone(todo))
            .Select(todo => todo.Id)
            .ToArray();
        if (blockingTodoIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Skill,
                HiringStageReadinessStatus.Partial,
                "技能阶段仍有待补充能力项未完成。",
                blockingTodoIds);
        }

        if (!runtimeFacts.SkillBaselineConfirmed)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.Skill,
                HiringStageReadinessStatus.Partial,
                activeRequiredTodos.Length == 0
                    ? "默认技能基线已满足，等待用户确认是否进入第三阶段。"
                    : "补充技能项已完成，等待用户确认技能阶段已足够。",
                []);
        }

        return new HiringStageReadinessDto(
            HiringCollectionStage.Skill,
            HiringStageReadinessStatus.Complete,
            activeRequiredTodos.Length == 0
                ? "默认技能基线已确认，无需新增补充技能项。"
                : "技能补充项已完成并确认。",
            activeRequiredTodos.Select(todo => todo.Id).ToArray());
    }

    private static HiringStageReadinessDto BuildExternalStageReadiness(HiringRuntimeContext runtimeContext)
    {
        if (ShouldApplyDemoPackagingShortcut(GetUploadedMaterialFileNames(runtimeContext.Materials)))
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Complete,
                "演示临时处理：上传资料后默认外部系统阶段已完备，可直接进入打包阶段。",
                []);
        }

        var externalTodos = runtimeContext.WorkflowTodos
            .Where(todo => string.Equals(todo.Stage, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var completedSkipTodo = externalTodos.FirstOrDefault(todo =>
            string.Equals(todo.Kind, HiringTodoKind.Gap, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(todo.GapType, "external_skip_declaration", StringComparison.OrdinalIgnoreCase) &&
            !IsTodoDismissed(todo) &&
            IsTodoDone(todo));
        if (completedSkipTodo is not null)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Skipped,
                "已明确当前雇佣流程无需外部连接能力。",
                [completedSkipTodo.Id]);
        }

        var activeRequiredTodos = externalTodos
            .Where(todo => IsRequiredGapTodo(todo) && !IsTodoDismissed(todo))
            .ToArray();
        if (activeRequiredTodos.Length == 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Missing,
                "外部阶段尚未明确任何连接能力。",
                []);
        }

        var pendingSkipTodoIds = activeRequiredTodos
            .Where(todo =>
                string.Equals(todo.GapType, "external_skip_declaration", StringComparison.OrdinalIgnoreCase) &&
                !IsTodoDone(todo))
            .Select(todo => todo.Id)
            .ToArray();
        if (pendingSkipTodoIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "已发起跳过外部阶段声明，等待确认。",
                pendingSkipTodoIds);
        }

        var connectorTodos = activeRequiredTodos
            .Where(todo => !string.Equals(todo.GapType, "external_skip_declaration", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (connectorTodos.Length == 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Missing,
                "外部阶段尚未形成可执行的连接能力项。",
                []);
        }

        var invalidPayloadTodoIds = new List<string>();
        var connectorCapabilities = new List<ExternalConnectorCapability>();
        foreach (var todo in connectorTodos)
        {
            if (!TryParseExternalConnectorPayload(todo.Payload, out var capability))
            {
                invalidPayloadTodoIds.Add(todo.Id);
                continue;
            }

            connectorCapabilities.Add(capability with { TodoId = todo.Id });
        }

        if (invalidPayloadTodoIds.Count > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "外部能力工单缺少完整的 connector payload。",
                invalidPayloadTodoIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        var blockingTodoIds = connectorTodos
            .Where(todo => !IsTodoDone(todo))
            .Select(todo => todo.Id)
            .ToArray();
        if (blockingTodoIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "外部阶段仍有连接能力待完成。",
                blockingTodoIds);
        }

        var pendingCredentialTodoIds = ResolvePendingExternalCredentialTodoIds(
            runtimeContext.CredentialSlots,
            connectorCapabilities);
        if (pendingCredentialTodoIds.Length > 0)
        {
            return new HiringStageReadinessDto(
                HiringCollectionStage.External,
                HiringStageReadinessStatus.Partial,
                "外部连接能力已明确，但仍有凭据槽位待绑定。",
                pendingCredentialTodoIds);
        }

        return new HiringStageReadinessDto(
            HiringCollectionStage.External,
            HiringStageReadinessStatus.Complete,
            "外部连接能力、操作目标与凭据绑定已全部就绪。",
            connectorTodos.Select(todo => todo.Id).ToArray());
    }

    private static string[] ResolvePendingExternalCredentialTodoIds(
        IReadOnlyList<HiringCredentialSlotDto> credentialSlots,
        IReadOnlyList<ExternalConnectorCapability> connectorCapabilities)
    {
        var pendingTodoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var capability in connectorCapabilities)
        {
            if (string.Equals(capability.AuthKind, "none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var slot = credentialSlots.FirstOrDefault(candidate =>
                string.Equals(candidate.CredentialSlot, capability.CredentialSlot, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                pendingTodoIds.Add(capability.TodoId);
                continue;
            }

            if (!string.Equals(slot.BindingStatus, HiringCredentialBindingStatus.Bound, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(slot.BindingStatus, HiringCredentialBindingStatus.NotRequired, StringComparison.OrdinalIgnoreCase))
            {
                pendingTodoIds.Add(string.IsNullOrWhiteSpace(slot.TodoId) ? capability.TodoId : slot.TodoId);
            }
        }

        return pendingTodoIds.ToArray();
    }

    private static bool TryParseExternalConnectorPayload(
        JsonElement? payload,
        out ExternalConnectorCapability capability)
    {
        capability = default!;
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var payloadValue = payload.Value;
        if (!TryReadPayloadString(payloadValue, "connector_type", out var connectorType) ||
            !TryReadPayloadString(payloadValue, "connector_name", out var connectorName) ||
            !TryReadPayloadString(payloadValue, "operation", out var operation) ||
            !TryReadPayloadString(payloadValue, "objective", out var objective) ||
            !TryReadPayloadString(payloadValue, "auth_kind", out var authKind) ||
            !TryReadPayloadStringArray(payloadValue, "linked_skills", out var linkedSkills) ||
            linkedSkills.Length == 0)
        {
            return false;
        }

        var hasCredentialSlot = TryReadPayloadString(payloadValue, "credential_slot", out var credentialSlot);
        if (!string.Equals(authKind, "none", StringComparison.OrdinalIgnoreCase) &&
            (!hasCredentialSlot || string.IsNullOrWhiteSpace(credentialSlot)))
        {
            return false;
        }

        capability = new ExternalConnectorCapability(
            TodoId: string.Empty,
            ConnectorType: connectorType,
            ConnectorName: connectorName,
            Operation: operation,
            Objective: objective,
            CredentialSlot: hasCredentialSlot ? credentialSlot : string.Empty,
            AuthKind: authKind,
            LinkedSkills: linkedSkills);
        return true;
    }

    private static bool TryReadPayloadString(JsonElement payload, string propertyName, out string value)
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

        value = string.Empty;
        return false;
    }

    private static bool TryReadPayloadStringArray(JsonElement payload, string propertyName, out string[] values)
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
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return values.Length > 0;
        }

        values = [];
        return false;
    }

    private static string[] GetUploadedMaterialFileNames(IReadOnlyList<HiringConversationMaterialDto> materials)
    {
        return materials
            .Where(material =>
                string.Equals(material.Type, "file", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(material.Name) &&
                !IsSystemMaterial(material))
            .Select(material => material.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSystemMaterial(HiringConversationMaterialDto material)
    {
        if (material.Metadata is null)
        {
            return false;
        }

        return material.Metadata.ContainsKey("referenceType");
    }


    public static string[] GetUploadedMaterialFileNamesForDiagnostics(IReadOnlyList<HiringConversationMaterialDto> materials)
        => GetUploadedMaterialFileNames(materials);

    private static bool ShouldApplyDemoPackagingShortcut(IReadOnlyList<string> uploadedMaterialFiles)
    {
        // Temporary demo shortcut: once at least one business file is uploaded,
        // skip stage-two and stage-three collection work and unlock packaging.
        return uploadedMaterialFiles.Count > 0;
    }

    private static string JoinPreview(IReadOnlyList<string> values)
    {
        const int previewCount = 3;
        if (values.Count <= previewCount)
        {
            return string.Join("、", values);
        }

        return $"{string.Join("、", values.Take(previewCount))} 等 {values.Count} 项";
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
                => "是否已经上传并分类至少 1 份资料，并为每份资料写明抽取目标？",
            var value when string.Equals(value, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)
                => "默认技能基线是否已确认，如有待补充能力项是否已全部完成？",
            var value when string.Equals(value, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase)
                => "外部连接能力是否已逐项完成，并完成所需凭据绑定或明确跳过？",
            _ => "仍有阶段未完成。"
        };
    }

    private static string BuildDiagnosticAction(string stage)
    {
        return stage switch
        {
            var value when string.Equals(value, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase)
                => "继续补齐资料分类与抽取目标。",
            var value when string.Equals(value, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)
                => "继续补齐待补充技能项，或确认技能阶段已足够。",
            var value when string.Equals(value, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase)
                => "继续补齐连接能力配置，或完成凭据绑定/跳过确认。",
            _ => "继续处理当前阻塞项。"
        };
    }

    private static string BuildDiagnosticSummary(string currentStage, bool readyForPackaging)
    {
        if (readyForPackaging)
        {
            return "资料、技能与外部连接均已就绪，可以进入打包。";
        }

        if (string.Equals(currentStage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase))
        {
            return "业务阶段已经完成，但仍有诊断或复核阻塞项需要处理。";
        }

        return $"当前还需要补齐{DisplayStage(currentStage)}阶段。";
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

        // Strip leading markdown code fence: ```json, ```, `json, ` etc.
        // Handles ```\n...\n```, ```...```, `...`, and variants.
        if (trimmed.StartsWith('`'))
        {
            var fenceLen = trimmed.StartsWith("```", StringComparison.Ordinal) ? 3 : 1;
            var afterFence = trimmed[fenceLen..];

            // Strip language tag (e.g. json, yaml, xml) if present
            var langEnd = 0;
            while (langEnd < afterFence.Length && char.IsLetterOrDigit(afterFence[langEnd]))
            {
                langEnd++;
            }
            afterFence = afterFence[langEnd..];

            // Strip optional newline after opening fence
            if (afterFence.StartsWith('\n'))
            {
                afterFence = afterFence[1..];
            }

            // Strip matching trailing fence
            var trailingFence = new string('`', fenceLen);
            if (afterFence.EndsWith(trailingFence, StringComparison.Ordinal))
            {
                // Only strip if trailing fence is at the very end (after optional whitespace/newlines)
                var contentBody = afterFence.TrimEnd();
                if (contentBody.EndsWith(trailingFence, StringComparison.Ordinal))
                {
                    contentBody = contentBody[..^fenceLen].TrimEnd();
                }
                afterFence = contentBody;
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

    [GeneratedRegex("<(?<tag>dispatch|dispatch_callback|diagnostic_report|config_governance_patch|workflow_stage_facts)>(?<content>[\\s\\S]*?)</\\k<tag>>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HiringTagRegex();

    [GeneratedRegex("(token|api[_-]?key|secret|password|connection[_-]?string)\\s*[:=]\\s*[\"']?[A-Za-z0-9_\\-:/+=]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SensitiveValueRegex();
}

internal sealed record ParsedHiringAssistantReply(
    string VisibleContent,
    IReadOnlyList<HiringDispatchCommand> DispatchCommands,
    IReadOnlyList<HiringDispatchCallbackPayload> DispatchCallbacks,
    HiringDiagnosticReportDto? DiagnosticReport,
    IReadOnlyList<HiringConfigGovernanceFileDto> ConfigGovernanceFiles,
    HiringWorkflowStageFactsUpdate? StageFacts);

internal sealed record HiringWorkflowStageFactsUpdate(
    bool? MaterialReady,
    IReadOnlyList<string>? MaterialClassifiedFiles,
    IReadOnlyDictionary<string, string>? MaterialExtractionTargets,
    bool? SkillBaselineReviewed,
    bool? SkillBaselineConfirmed);

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

internal sealed record ExternalConnectorCapability(
    string TodoId,
    string ConnectorType,
    string ConnectorName,
    string Operation,
    string Objective,
    string CredentialSlot,
    string AuthKind,
    IReadOnlyList<string> LinkedSkills);
