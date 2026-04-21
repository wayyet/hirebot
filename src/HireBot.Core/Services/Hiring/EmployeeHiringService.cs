using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, HiringRuntimeState> hiringStates = new();

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

        var template = await templateDataProvider.GetByIdAsync(templateId.Trim(), cancellationToken);
        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        var hireId = $"hire_{Guid.NewGuid():N}";
        var sandboxId = $"sandbox_{Guid.NewGuid():N}";
        var status = HiringStatus.CreatingSandbox;

        var state = new HiringRuntimeState(
            hireId,
            sandboxId,
            status,
            null,
            null);

        hiringStates[hireId] = state;

        var shouldFail = request.UseCase?.Contains("simulate-skill-failure", StringComparison.OrdinalIgnoreCase) == true;

        // 雇佣接口仅负责接单，实际 Skill 加载由后台异步推进。
        _ = RunHiringWorkflowAsync(hireId, shouldFail);

        logger.LogInformation(
            "创建雇佣流程成功: HireId={HireId}, TemplateId={TemplateId}, TenantId={TenantId}",
            hireId,
            templateId,
            request.TenantId);

        var result = new HireTemplateResultDto(
            hireId,
            sandboxId,
            status,
            $"/api/v1/hirings/{hireId}");

        return ApiResponse<HireTemplateResultDto>.SuccessResponse(result, "雇佣任务已创建");
    }

    public Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId) || !hiringStates.TryGetValue(hireId, out var state))
        {
            return Task.FromResult(ApiResponse<HiringStatusDto>.ErrorResponse(404, "雇佣流程不存在"));
        }

        var result = new HiringStatusDto(
            state.HireId,
            state.SandboxId,
            state.Status,
            state.ErrorCode,
            state.ErrorMessage);

        return Task.FromResult(ApiResponse<HiringStatusDto>.SuccessResponse(result));
    }

    private async Task RunHiringWorkflowAsync(string hireId, bool shouldFail)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            UpdateStatus(hireId, HiringStatus.SkillLoading, null, null);

            await Task.Delay(TimeSpan.FromSeconds(2));

            if (shouldFail)
            {
                UpdateStatus(hireId, HiringStatus.Failed, "SKILL_BOOTSTRAP_FAILED", "Skill 流程加载失败，请稍后重试");
                logger.LogWarning("雇佣流程失败: HireId={HireId}", hireId);
                return;
            }

            UpdateStatus(hireId, HiringStatus.Ready, null, null);
            logger.LogInformation("雇佣流程就绪: HireId={HireId}", hireId);
        }
        catch (Exception ex)
        {
            UpdateStatus(hireId, HiringStatus.Failed, "UNEXPECTED_ERROR", ex.Message);
            logger.LogError(ex, "雇佣流程执行异常: HireId={HireId}", hireId);
        }
    }

    private void UpdateStatus(string hireId, string status, string? errorCode, string? errorMessage)
    {
        if (!hiringStates.TryGetValue(hireId, out var current))
        {
            return;
        }

        hiringStates[hireId] = current with
        {
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }

    private sealed record HiringRuntimeState(
        string HireId,
        string SandboxId,
        string Status,
        string? ErrorCode,
        string? ErrorMessage);
}
