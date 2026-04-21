using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

public sealed class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    ILogger<EmployeeHiringService> logger) : IEmployeeHiringService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly StageDef[] Stages =
    [
        new(HiringCollectionStage.Goal, "skill.hiring.goal-collector", ["business_goal", "owner", "success_metric"], "收集业务目标"),
        new(HiringCollectionStage.Scenario, "skill.hiring.scenario-designer", ["user_profile", "trigger_event", "expected_outcome"], "沉淀关键场景"),
        new(HiringCollectionStage.Systems, "skill.hiring.system-integrator", ["system_list", "permission_scope", "data_sources"], "确认系统与权限"),
        new(HiringCollectionStage.Gaps, "skill.hiring.gap-analyzer", ["blockers", "risk_level", "fallback_plan"], "识别能力缺口"),
        new(HiringCollectionStage.Package, "skill.hiring.package-builder", ["runbook", "acceptance_criteria", "delivery_window"], "输出交付包"),
    ];

    private static readonly HashSet<string> Decisions = new(StringComparer.OrdinalIgnoreCase)
    {
        HiringAuditDecision.Approve,
        HiringAuditDecision.RequestChanges,
        HiringAuditDecision.RollbackToStage,
        HiringAuditDecision.ForceOverride
    };

    private readonly ConcurrentDictionary<string, State> states = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ApiResponse<HireTemplateResultDto>> HireAsync(
        string templateId,
        HireTemplateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(400, "templateId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.OperatorId))
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(400, "tenantId 和 operatorId 为必填项");
        }

        var normalizedTemplateId = templateId.Trim();
        var template = await templateDataProvider.GetByIdAsync(normalizedTemplateId, cancellationToken);
        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        var hireId = $"hire_{Guid.NewGuid():N}";
        var sandboxId = $"sandbox_{Guid.NewGuid():N}";
        states[hireId] = new State
        {
            HireId = hireId,
            SandboxId = sandboxId,
            Status = HiringStatus.CreatingSandbox,
            CollectionPhase = HiringCollectionPhase.NotStarted,
            CurrentStage = HiringCollectionStage.Goal
        };

        var shouldFail = request.UseCase?.Contains("simulate-skill-failure", StringComparison.OrdinalIgnoreCase) == true;
        _ = RunHiringWorkflowAsync(hireId, shouldFail, CancellationToken.None);

        logger.LogInformation("创建雇佣流程成功: HireId={HireId}, TemplateId={TemplateId}, TenantId={TenantId}",
            hireId, normalizedTemplateId, request.TenantId);

        var result = new HireTemplateResultDto(hireId, sandboxId, HiringStatus.CreatingSandbox, $"/api/v1/hirings/{hireId}");
        return ApiResponse<HireTemplateResultDto>.SuccessResponse(result, "雇佣任务已创建");
    }

    public Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<HiringStatusDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var dto = new HiringStatusDto(state.HireId, state.SandboxId, state.Status, state.ErrorCode, state.ErrorMessage, state.CollectionPhase, state.CurrentStage);
            return Task.FromResult(ApiResponse<HiringStatusDto>.SuccessResponse(dto));
        }
    }

    public Task<ApiResponse<StartHiringConversationResultDto>> StartConversationAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<StartHiringConversationResultDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<StartHiringConversationResultDto>(state);
            if (gate is not null)
            {
                return Task.FromResult(gate);
            }

            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                state.SessionId = $"session_{Guid.NewGuid():N}";
                AddMessage(state, "assistant", "会话已启动，请从 GOAL 阶段开始提供信息。");
            }

            state.CollectionPhase = HiringCollectionPhase.InProgress;
            state.CurrentStage = string.IsNullOrWhiteSpace(state.CurrentStage) ? HiringCollectionStage.Goal : state.CurrentStage;
            state.RequiresAudit = false;

            var result = new StartHiringConversationResultDto(state.HireId, state.SessionId!, state.CurrentStage, state.RequiresAudit, BuildMappings());
            return Task.FromResult(ApiResponse<StartHiringConversationResultDto>.SuccessResponse(result, "会话初始化成功"));
        }
    }
    public Task<ApiResponse<HiringConversationResultDto>> SendConversationMessageAsync(
        string hireId,
        HiringConversationMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<HiringConversationResultDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        if (request is null || (string.IsNullOrWhiteSpace(request.Content) && (request.StructuredAnswers is null || request.StructuredAnswers.Count == 0)))
        {
            return Task.FromResult(ApiResponse<HiringConversationResultDto>.ErrorResponse(400, "content 与 structuredAnswers 不能同时为空"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<HiringConversationResultDto>(state);
            if (gate is not null)
            {
                return Task.FromResult(gate);
            }

            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                return Task.FromResult(ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "会话尚未启动"));
            }

            if (state.RequiresAudit)
            {
                return Task.FromResult(ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "当前阶段待审计，请先提交审计决策"));
            }

            if (string.Equals(state.CurrentStage, HiringCollectionStage.Done, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "所有阶段已完成，请执行 finalize"));
            }

            var stage = FindStage(state.CurrentStage);
            if (stage is null)
            {
                return Task.FromResult(ApiResponse<HiringConversationResultDto>.ErrorResponse(500, "阶段配置异常"));
            }

            ResetArtifacts(state);
            AddMessage(state, "user", request.Content ?? string.Empty);

            var data = InitData(state, stage);
            FillData(data, stage, request.StructuredAnswers, request.Content ?? string.Empty);
            var missing = stage.RequiredFields.Where(x => string.IsNullOrWhiteSpace(data[x])).ToArray();
            if (missing.Length == stage.RequiredFields.Count && !string.IsNullOrWhiteSpace(request.Content))
            {
                data[stage.RequiredFields[0]] = request.Content.Trim();
                missing = stage.RequiredFields.Skip(1).ToArray();
            }

            var risk = new List<string>();
            if (missing.Length > 0)
            {
                risk.Add($"缺少关键字段：{string.Join("、", missing)}");
            }
            if ((request.Content ?? string.Empty).Contains("不确定", StringComparison.OrdinalIgnoreCase))
            {
                risk.Add("存在不确定描述，建议二次确认。");
            }
            if (risk.Count == 0)
            {
                risk.Add("未发现明显风险。");
            }

            var summaryItems = stage.RequiredFields
                .Where(x => !string.IsNullOrWhiteSpace(data[x]))
                .Take(3)
                .Select(x => $"{x}={data[x]}")
                .ToArray();
            var summary = summaryItems.Length == 0
                ? $"阶段 {stage.Stage} 暂无结构化信息，请继续补充。"
                : $"阶段 {stage.Stage} 预览：" + string.Join("；", summaryItems);

            var preview = new HiringStagePreviewDto(
                state.HireId,
                stage.Stage,
                stage.SkillName,
                summary,
                new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase),
                missing,
                risk,
                missing.Length == 0,
                DateTimeOffset.UtcNow);

            state.StagePreviews[stage.Stage] = preview;
            state.RequiresAudit = preview.ReadyForAudit;
            state.CollectionPhase = HiringCollectionPhase.InProgress;

            var assistant = AddMessage(
                state,
                "assistant",
                preview.ReadyForAudit
                    ? $"阶段 {stage.Stage} 已生成预览，可进入审计。Skill={stage.SkillName}"
                    : $"阶段 {stage.Stage} 仍需补充：{string.Join("、", preview.MissingFields)}");

            var result = new HiringConversationResultDto(state.HireId, state.SessionId!, state.CurrentStage, state.RequiresAudit, assistant, preview);
            return Task.FromResult(ApiResponse<HiringConversationResultDto>.SuccessResponse(result, "阶段消息已处理"));
        }
    }

    public Task<ApiResponse<HiringConversationTimelineDto>> GetConversationTimelineAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<HiringConversationTimelineDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<HiringConversationTimelineDto>(state);
            if (gate is not null)
            {
                return Task.FromResult(gate);
            }

            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                return Task.FromResult(ApiResponse<HiringConversationTimelineDto>.ErrorResponse(409, "会话尚未启动"));
            }

            var result = new HiringConversationTimelineDto(
                state.HireId,
                state.SessionId!,
                state.CurrentStage,
                state.RequiresAudit,
                state.CollectionPhase,
                state.Messages.ToArray(),
                BuildMappings());
            return Task.FromResult(ApiResponse<HiringConversationTimelineDto>.SuccessResponse(result));
        }
    }

    public Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(string hireId, string? stage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<HiringStagePreviewDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<HiringStagePreviewDto>(state);
            if (gate is not null)
            {
                return Task.FromResult(gate);
            }

            var key = string.IsNullOrWhiteSpace(stage) ? state.CurrentStage : Norm(stage);
            if (!state.StagePreviews.TryGetValue(key, out var preview))
            {
                return Task.FromResult(ApiResponse<HiringStagePreviewDto>.ErrorResponse(404, "当前阶段尚未生成预览"));
            }

            return Task.FromResult(ApiResponse<HiringStagePreviewDto>.SuccessResponse(preview));
        }
    }
    public Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(
        string hireId,
        HiringAuditDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        if (request is null)
        {
            return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, "请求体不能为空"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<HiringAuditDecisionResultDto>(state);
            if (gate is not null)
            {
                return Task.FromResult(gate);
            }

            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(409, "会话尚未启动"));
            }

            var stage = Norm(request.Stage);
            var decision = Norm(request.Decision);
            if (!Decisions.Contains(decision))
            {
                return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, "不支持的审计决策"));
            }

            if (!string.Equals(stage, state.CurrentStage, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(409, "审计阶段必须与当前阶段一致"));
            }

            if (!state.StagePreviews.TryGetValue(stage, out var preview))
            {
                return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(409, "当前阶段尚未生成可审计预览"));
            }

            if (!state.RequiresAudit)
            {
                return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(409, "当前阶段未进入审计态"));
            }

            if (decision == HiringAuditDecision.Approve && !preview.ReadyForAudit)
            {
                return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(409, "阶段信息未补齐，请补齐或使用 FORCE_OVERRIDE"));
            }

            if (decision == HiringAuditDecision.RollbackToStage)
            {
                var target = Norm(request.RollbackTargetStage);
                if (string.IsNullOrWhiteSpace(target))
                {
                    return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, "rollbackTargetStage 不能为空"));
                }

                if (StageIndex(target) < 0 || target == HiringCollectionStage.Done || StageIndex(target) > StageIndex(state.CurrentStage))
                {
                    return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, "rollbackTargetStage 非法"));
                }
            }

            ResetArtifacts(state);
            if (decision == HiringAuditDecision.RequestChanges)
            {
                state.RequiresAudit = false;
                AddMessage(state, "assistant", $"审计驳回，请在阶段 {state.CurrentStage} 补充信息后重新提交。");
            }
            else if (decision == HiringAuditDecision.RollbackToStage)
            {
                var target = Norm(request.RollbackTargetStage)!;
                state.CurrentStage = target;
                state.CollectionPhase = HiringCollectionPhase.InProgress;
                state.RequiresAudit = false;
                var targetIndex = StageIndex(target);
                var removeStages = Stages.Select(x => x.Stage).Where(x => StageIndex(x) > targetIndex).ToArray();
                foreach (var item in removeStages)
                {
                    state.StagePreviews.Remove(item);
                }
                state.AuditLogs.RemoveAll(x => StageIndex(x.Stage) > targetIndex);
                AddMessage(state, "assistant", $"流程已回退到阶段：{state.CurrentStage}。");
            }
            else
            {
                var idx = StageIndex(state.CurrentStage);
                if (idx < 0 || idx >= Stages.Length - 1)
                {
                    state.CurrentStage = HiringCollectionStage.Done;
                    state.CollectionPhase = HiringCollectionPhase.ReadyForFinalize;
                }
                else
                {
                    state.CurrentStage = Stages[idx + 1].Stage;
                    state.CollectionPhase = HiringCollectionPhase.InProgress;
                }
                state.RequiresAudit = false;
                AddMessage(
                    state,
                    "assistant",
                    state.CurrentStage == HiringCollectionStage.Done
                        ? "所有阶段已通过审计，可执行 finalize 生成交付物。"
                        : $"审计通过，已进入下一阶段：{state.CurrentStage}");
            }

            var inputDigest = Hash(JsonSerializer.Serialize(preview.StructuredData, JsonOptions));
            var outputDigest = Hash($"{decision}|{request.Comment}|{state.CurrentStage}|{state.CollectionPhase}");
            state.AuditLogs.Add(new HiringAuditLogDto(
                $"audit_{Guid.NewGuid():N}",
                preview.Stage,
                preview.SkillName,
                decision,
                "operator",
                string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
                inputDigest,
                outputDigest,
                DateTimeOffset.UtcNow));

            var result = new HiringAuditDecisionResultDto(state.HireId, stage, decision, state.CurrentStage, state.RequiresAudit, state.CollectionPhase);
            return Task.FromResult(ApiResponse<HiringAuditDecisionResultDto>.SuccessResponse(result, "审计决策已记录"));
        }
    }

    public Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<IReadOnlyList<HiringAuditLogDto>>.ErrorResponse(404, "雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var result = state.AuditLogs.OrderByDescending(x => x.TimestampUtc).ToArray();
            return Task.FromResult(ApiResponse<IReadOnlyList<HiringAuditLogDto>>.SuccessResponse(result));
        }
    }
    public Task<ApiResponse<HiringFinalizeResultDto>> FinalizeAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<HiringFinalizeResultDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<HiringFinalizeResultDto>(state);
            if (gate is not null)
            {
                return Task.FromResult(gate);
            }

            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                return Task.FromResult(ApiResponse<HiringFinalizeResultDto>.ErrorResponse(409, "会话尚未启动"));
            }

            if (state.CollectionPhase != HiringCollectionPhase.ReadyForFinalize || state.CurrentStage != HiringCollectionStage.Done)
            {
                return Task.FromResult(ApiResponse<HiringFinalizeResultDto>.ErrorResponse(409, "阶段尚未全部完成，无法 finalize"));
            }

            var orderedPreviews = state.StagePreviews.Values.OrderBy(x => StageIndex(x.Stage)).ToArray();
            var timeline = new
            {
                state.HireId,
                state.SandboxId,
                state.SessionId,
                state.CollectionPhase,
                state.CurrentStage,
                Messages = state.Messages,
                StagePreviews = orderedPreviews
            };

            state.ArtifactFiles.Clear();
            state.ArtifactFiles["stage-skill-mapping.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(BuildMappings(), JsonOptions));
            state.ArtifactFiles["conversation-timeline.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(timeline, JsonOptions));
            state.ArtifactFiles["audit-log.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state.AuditLogs, JsonOptions));
            var md = new StringBuilder();
            md.AppendLine("# HireBot 阶段交付摘要");
            md.AppendLine($"- HireId: {state.HireId}");
            md.AppendLine($"- SandboxId: {state.SandboxId}");
            md.AppendLine($"- SessionId: {state.SessionId}");
            md.AppendLine($"- CollectionPhase: {state.CollectionPhase}");
            md.AppendLine($"- CurrentStage: {state.CurrentStage}");
            md.AppendLine();
            md.AppendLine("## 阶段预览");
            foreach (var p in orderedPreviews)
            {
                md.AppendLine($"### {p.Stage} ({p.SkillName})");
                md.AppendLine(p.Summary);
                md.AppendLine($"- MissingFields: {(p.MissingFields.Count == 0 ? "无" : string.Join("、", p.MissingFields))}");
                md.AppendLine($"- RiskNotes: {string.Join("；", p.RiskNotes)}");
                md.AppendLine();
            }
            state.ArtifactFiles["handover.md"] = Encoding.UTF8.GetBytes(md.ToString());

            state.CollectionPhase = HiringCollectionPhase.Finalized;
            state.RequiresAudit = false;

            var result = new HiringFinalizeResultDto(
                state.HireId,
                state.CurrentStage,
                state.CollectionPhase,
                state.ArtifactFiles.Keys.ToArray(),
                $"/api/v1/hirings/{state.HireId}/artifacts/download");
            return Task.FromResult(ApiResponse<HiringFinalizeResultDto>.SuccessResponse(result, "交付物已生成"));
        }
    }

    public Task<ApiResponse<HiringWorkflowStateDto>> GetWorkflowStateAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(ApiResponse<HiringWorkflowStateDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<HiringWorkflowStateDto>(state);
            if (gate is not null)
            {
                return Task.FromResult(gate);
            }

            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                return Task.FromResult(ApiResponse<HiringWorkflowStateDto>.ErrorResponse(409, "会话尚未启动"));
            }

            var dto = new HiringWorkflowStateDto(
                state.HireId,
                state.SessionId!,
                state.CurrentStage,
                state.RequiresAudit,
                state.CollectionPhase,
                BuildMappings(),
                state.AuditLogs.OrderByDescending(x => x.TimestampUtc).ToArray());
            return Task.FromResult(ApiResponse<HiringWorkflowStateDto>.SuccessResponse(dto));
        }
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !states.TryGetValue(hireId.Trim(), out var state))
        {
            return Task.FromResult(HiringArtifactDownloadResult.NotFound("雇佣流程不存在"));
        }

        lock (state.SyncRoot)
        {
            var gate = EnsureReady<HiringArtifactDownloadResult>(state);
            if (gate is not null)
            {
                return Task.FromResult(HiringArtifactDownloadResult.Error(gate.Code, gate.Message));
            }

            if (state.CollectionPhase != HiringCollectionPhase.Finalized)
            {
                return Task.FromResult(HiringArtifactDownloadResult.Error(409, "流程未 finalize，暂无可下载交付物"));
            }

            if (state.ArtifactFiles.Count == 0)
            {
                return Task.FromResult(HiringArtifactDownloadResult.Error(409, "交付物为空，请重新执行 finalize"));
            }

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var file in state.ArtifactFiles)
                {
                    var entry = zip.CreateEntry(file.Key, CompressionLevel.Fastest);
                    using var stream = entry.Open();
                    stream.Write(file.Value, 0, file.Value.Length);
                }
            }

            return Task.FromResult(HiringArtifactDownloadResult.Success($"{state.HireId}_artifacts.zip", "application/zip", ms.ToArray()));
        }
    }
    private async Task RunHiringWorkflowAsync(string hireId, bool shouldFail, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            UpdateStatus(hireId, HiringStatus.SkillLoading, null, null);

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            if (shouldFail)
            {
                UpdateStatus(hireId, HiringStatus.Failed, "SKILL_BOOTSTRAP_FAILED", "Skill 流程加载失败，请稍后重试");
                logger.LogWarning("雇佣流程失败: HireId={HireId}", hireId);
                return;
            }

            UpdateStatus(hireId, HiringStatus.Ready, null, null);
            logger.LogInformation("雇佣流程就绪: HireId={HireId}", hireId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("雇佣流程被取消: HireId={HireId}", hireId);
        }
        catch (Exception ex)
        {
            UpdateStatus(hireId, HiringStatus.Failed, "UNEXPECTED_ERROR", ex.Message);
            logger.LogError(ex, "雇佣流程执行异常: HireId={HireId}", hireId);
        }
    }

    private void UpdateStatus(string hireId, string status, string? errorCode, string? errorMessage)
    {
        if (!states.TryGetValue(hireId, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            state.Status = status;
            state.ErrorCode = errorCode;
            state.ErrorMessage = errorMessage;
        }
    }

    private static ApiResponse<T>? EnsureReady<T>(State state)
    {
        if (state.Status == HiringStatus.Failed)
        {
            return ApiResponse<T>.ErrorResponse(409, "雇佣流程已失败，请重新发起雇佣");
        }

        if (state.Status != HiringStatus.Ready)
        {
            return ApiResponse<T>.ErrorResponse(409, "雇佣流程尚未就绪，请稍后重试");
        }

        return null;
    }

    private static HiringConversationMessageDto AddMessage(State state, string role, string content)
    {
        var message = new HiringConversationMessageDto($"msg_{Guid.NewGuid():N}", role, string.IsNullOrWhiteSpace(content) ? "（空）" : content.Trim(), DateTimeOffset.UtcNow);
        state.Messages.Add(message);
        return message;
    }

    private static StageDef? FindStage(string stage)
    {
        return Stages.FirstOrDefault(x => string.Equals(x.Stage, stage, StringComparison.OrdinalIgnoreCase));
    }

    private static StageSkillMappingDto[] BuildMappings()
    {
        return Stages.Select(x => new StageSkillMappingDto(x.Stage, x.SkillName, x.RequiredFields.ToArray(), x.Description)).ToArray();
    }

    private static string Norm(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    }

    private static int StageIndex(string stage)
    {
        for (var i = 0; i < Stages.Length; i++)
        {
            if (string.Equals(Stages[i].Stage, stage, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return stage == HiringCollectionStage.Done ? Stages.Length : -1;
    }

    private static string Hash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static Dictionary<string, string?> InitData(State state, StageDef stage)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in stage.RequiredFields)
        {
            data[key] = null;
        }

        if (state.StagePreviews.TryGetValue(stage.Stage, out var existed))
        {
            foreach (var item in existed.StructuredData)
            {
                if (data.ContainsKey(item.Key))
                {
                    data[item.Key] = item.Value;
                }
            }
        }

        return data;
    }

    private static void FillData(
        IDictionary<string, string?> data,
        StageDef stage,
        IReadOnlyDictionary<string, string>? answers,
        string content)
    {
        if (answers is not null)
        {
            foreach (var answer in answers)
            {
                var key = stage.RequiredFields.FirstOrDefault(x => string.Equals(x, answer.Key, StringComparison.OrdinalIgnoreCase));
                if (key is not null)
                {
                    data[key] = string.IsNullOrWhiteSpace(answer.Value) ? null : answer.Value.Trim();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf(':');
            if (idx < 0)
            {
                idx = line.IndexOf('：');
            }

            if (idx <= 0 || idx >= line.Length - 1)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var target = stage.RequiredFields.FirstOrDefault(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                data[target] = value;
            }
        }
    }

    private static void ResetArtifacts(State state)
    {
        if (state.ArtifactFiles.Count > 0)
        {
            state.ArtifactFiles.Clear();
        }

        if (state.CollectionPhase == HiringCollectionPhase.Finalized)
        {
            state.CollectionPhase = HiringCollectionPhase.InProgress;
        }
    }

    private sealed class State
    {
        public string HireId { get; init; } = string.Empty;
        public string SandboxId { get; init; } = string.Empty;
        public string Status { get; set; } = HiringStatus.CreatingSandbox;
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string CollectionPhase { get; set; } = HiringCollectionPhase.NotStarted;
        public string CurrentStage { get; set; } = HiringCollectionStage.Goal;
        public string? SessionId { get; set; }
        public bool RequiresAudit { get; set; }
        public List<HiringConversationMessageDto> Messages { get; } = [];
        public Dictionary<string, HiringStagePreviewDto> StagePreviews { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<HiringAuditLogDto> AuditLogs { get; } = [];
        public Dictionary<string, byte[]> ArtifactFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public object SyncRoot { get; } = new();
    }

    private sealed record StageDef(string Stage, string SkillName, IReadOnlyList<string> RequiredFields, string Description);
}
