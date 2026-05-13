using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;

namespace HireBot.Core.Services.Hiring;

internal sealed class HiringTodoService(IHiringRuntimeStore hiringRuntimeStore) : IHiringTodoService
{
    public Task<ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>> GetTodosAsync(
        string sessionId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.FromResult(ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.ErrorResponse(400, "sessionId 不能为空"));

        var context = hiringRuntimeStore.GetBySessionId(sessionId);
        if (context is null)
            return Task.FromResult(ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.ErrorResponse(404, $"找不到会话 {sessionId} 对应的雇佣上下文"));

        // 验证请求者身份：只有 OwnerSubject 匹配才允许访问
        if (!string.Equals(context.OwnerSubject, requestingUserId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.ErrorResponse(403, "无权访问此雇佣会话"));

        return Task.FromResult(ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.SuccessResponse(context.HandoffItems, "获取成功"));
    }

    public Task<ApiResponse<HiringWorkflowHandoffDto>> UpsertTodoAsync(
        string sessionId,
        string requestingUserId,
        UpsertHiringTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(400, "sessionId 不能为空"));

        if (string.IsNullOrWhiteSpace(request.HandoffId))
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(400, "handoffId 不能为空"));

        var context = hiringRuntimeStore.GetBySessionId(sessionId);
        if (context is null)
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(404, $"找不到会话 {sessionId} 对应的雇佣上下文"));

        if (!string.Equals(context.OwnerSubject, requestingUserId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(403, "无权访问此雇佣会话"));

        var now = DateTimeOffset.UtcNow;
        var existing = context.HandoffItems.FirstOrDefault(h =>
            string.Equals(h.HandoffId, request.HandoffId, StringComparison.OrdinalIgnoreCase));

        var updatedItem = new HiringWorkflowHandoffDto(
            SessionId: context.SessionId,
            WorkflowId: context.HireId,
            HandoffId: request.HandoffId,
            Title: request.Title,
            Kind: request.Kind,
            Stage: request.Stage,
            TargetSkill: request.TargetSkill,
            Intent: request.Intent,
            Category: request.Category,
            Payload: request.Payload ?? JsonSerializer.SerializeToElement(new { }),
            Source: request.Source,
            Acceptance: request.Acceptance,
            Status: request.Status,
            Fingerprint: ComputeFingerprint(request.Title, request.Kind, request.Stage, request.TargetSkill),
            RelatedHandoffIds: request.RelatedHandoffIds ?? [],
            RelatedFiles: request.RelatedFiles ?? [],
            Revision: existing is null ? 1 : existing.Revision + 1,
            CreatedAtUtc: existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc: now,
            DispatchId: null,
            CallbackSummary: null);

        // 用 LINQ 保留所有其他 item，替换或追加本次 item
        var updatedList = context.HandoffItems
            .Where(h => !string.Equals(h.HandoffId, request.HandoffId, StringComparison.OrdinalIgnoreCase))
            .Append(updatedItem)
            .ToList();

        hiringRuntimeStore.Upsert(context with { HandoffItems = updatedList });

        return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.SuccessResponse(updatedItem, "已保存"));
    }

    /// <summary>基于内容计算指纹（用于幂等检测），取 SHA-256 前 16 个十六进制字符。</summary>
    private static string ComputeFingerprint(string title, string kind, string stage, string targetSkill)
    {
        var raw = $"{title}|{kind}|{stage}|{targetSkill}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public Task<ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>> GetTodosByHireIdAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId))
            return Task.FromResult(ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.ErrorResponse(400, "hireId 不能为空"));

        var context = hiringRuntimeStore.Get(hireId);
        if (context is null)
            return Task.FromResult(ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.SuccessResponse([], "暂无数据（会话尚未初始化）"));

        return Task.FromResult(ApiResponse<IReadOnlyList<HiringWorkflowHandoffDto>>.SuccessResponse(context.HandoffItems, "获取成功"));
    }

    public Task<ApiResponse<HiringWorkflowHandoffDto>> UpdateTodoStatusAsync(
        string hireId,
        string handoffId,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId))
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(400, "hireId 不能为空"));

        if (string.IsNullOrWhiteSpace(handoffId))
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(400, "handoffId 不能为空"));

        // 只允许用户切换为 confirmed 或 dismissed
        var allowedStatuses = new[] { HiringHandoffStatus.Confirmed, HiringHandoffStatus.Dismissed, HiringHandoffStatus.ReadyToDispatch };
        if (!allowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(400, $"不支持的状态：{status}，允许：confirmed / dismissed / ready_to_dispatch"));

        var context = hiringRuntimeStore.Get(hireId);
        if (context is null)
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(404, $"找不到 hireId={hireId} 对应的雇佣上下文"));

        var existing = context.HandoffItems.FirstOrDefault(h =>
            string.Equals(h.HandoffId, handoffId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.ErrorResponse(404, $"找不到 handoffId={handoffId}"));

        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            Status = status,
            Revision = existing.Revision + 1,
            UpdatedAtUtc = now,
        };

        var updatedList = context.HandoffItems
            .Where(h => !string.Equals(h.HandoffId, handoffId, StringComparison.OrdinalIgnoreCase))
            .Append(updated)
            .ToList();

        hiringRuntimeStore.Upsert(context with { HandoffItems = updatedList });

        return Task.FromResult(ApiResponse<HiringWorkflowHandoffDto>.SuccessResponse(updated, "状态已更新"));
    }
}
