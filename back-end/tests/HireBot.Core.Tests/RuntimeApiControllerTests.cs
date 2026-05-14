using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;
using HireBot.Abstraction.Models.Migration;
using HireBot.Abstraction.Models.Training;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Training;
using HireBot.ApiService.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.Core.Tests;

public sealed class RuntimeApiControllerTests
{
  

    

    private static EmployeeDetailDto BuildEmployee(string id, string type, string status, string? fromInstanceId)
    {
        return new EmployeeDetailDto(
            id,
            "name",
            "role",
            "template",
            "tpl",
            type,
            status,
            "tpl",
            fromInstanceId,
            "owner-1",
            "dept",
            status,
            "summary",
            "ok",
            "ok",
            "team",
            "2026-04-29",
            null,
            null,
            0,
            0,
            null,
            [],
            [],
            null,
            null,
            null,
            true);
    }

    private sealed class FakeInstanceChatService : IInstanceChatService
    {
        public string? GetInstanceId { get; private set; }
        public string? SendInstanceId { get; private set; }
        public SendInstanceChatMessageRequestDto? SendRequest { get; private set; }
        public string? ClearInstanceId { get; private set; }
        public string? GetEffectiveFeishuInstanceId { get; private set; }
        public string? ClearFeishuOverrideInstanceId { get; private set; }
        public string? UpdateDingTalkInstanceId { get; private set; }
        public DingTalkChannelConfig? UpdateDingTalkRequest { get; private set; }
        public string? GetEffectiveDingTalkInstanceId { get; private set; }
        public string? ClearDingTalkOverrideInstanceId { get; private set; }

        public ApiResponse<InstanceChatTimelineDto> GetResponse { get; init; } =
            ApiResponse<InstanceChatTimelineDto>.ErrorResponse(500, "not configured");

        public ApiResponse<InstanceChatResultDto> SendResponse { get; init; } =
            ApiResponse<InstanceChatResultDto>.ErrorResponse(500, "not configured");

        public ApiResponse<bool> ClearResponse { get; init; } =
            ApiResponse<bool>.ErrorResponse(500, "not configured");

        public ApiResponse<FeishuChannelEffectiveConfigDto> GetEffectiveFeishuResponse { get; init; } =
            ApiResponse<FeishuChannelEffectiveConfigDto>.ErrorResponse(500, "not configured");

        public ApiResponse<bool> ClearFeishuOverrideResponse { get; init; } =
            ApiResponse<bool>.ErrorResponse(500, "not configured");

        public ApiResponse<ImConfigResultDto> UpdateDingTalkResponse { get; init; } =
            ApiResponse<ImConfigResultDto>.ErrorResponse(500, "not configured");

        public ApiResponse<DingTalkChannelConfig> GetEffectiveDingTalkResponse { get; init; } =
            ApiResponse<DingTalkChannelConfig>.ErrorResponse(500, "not configured");

        public ApiResponse<bool> ClearDingTalkOverrideResponse { get; init; } =
            ApiResponse<bool>.ErrorResponse(500, "not configured");

        public Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            GetInstanceId = instanceId;
            return Task.FromResult(GetResponse);
        }

        public Task<ApiResponse<InstanceChatResultDto>> SendMessageAsync(string instanceId, SendInstanceChatMessageRequestDto request, CancellationToken cancellationToken = default)
        {
            SendInstanceId = instanceId;
            SendRequest = request;
            return Task.FromResult(SendResponse);
        }

        public Task<ApiResponse<bool>> ClearMessagesAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            ClearInstanceId = instanceId;
            return Task.FromResult(ClearResponse);
        }

        public Task<ApiResponse<ImConfigResultDto>> UpdateFeishuChannelConfigAsync(
            string instanceId,
            ImConfigRequestDto request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<ImConfigResultDto>.ErrorResponse(501, "not configured"));
        }

        public Task<ApiResponse<ImConfigResultDto>> UpdateDingTalkChannelConfigAsync(
            string instanceId,
            DingTalkChannelConfig request,
            CancellationToken cancellationToken = default)
        {
            UpdateDingTalkInstanceId = instanceId;
            UpdateDingTalkRequest = request;
            return Task.FromResult(UpdateDingTalkResponse);
        }

        public Task<ApiResponse<FeishuChannelEffectiveConfigDto>> GetFeishuChannelEffectiveConfigAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            GetEffectiveFeishuInstanceId = instanceId;
            return Task.FromResult(GetEffectiveFeishuResponse);
        }

        public Task<ApiResponse<DingTalkChannelConfig>> GetDingTalkChannelEffectiveConfigAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            GetEffectiveDingTalkInstanceId = instanceId;
            return Task.FromResult(GetEffectiveDingTalkResponse);
        }

        public Task<ApiResponse<bool>> ClearFeishuChannelOverrideAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            ClearFeishuOverrideInstanceId = instanceId;
            return Task.FromResult(ClearFeishuOverrideResponse);
        }

        public Task<ApiResponse<bool>> ClearDingTalkChannelOverrideAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            ClearDingTalkOverrideInstanceId = instanceId;
            return Task.FromResult(ClearDingTalkOverrideResponse);
        }

        public Task<ApiResponse<ImConfigResultDto>> UpdateWeComChannelConfigAsync(string instanceId, ImConfigRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<ImConfigResultDto>.ErrorResponse(500, "not configured"));

        public Task<ApiResponse<WeComChannelEffectiveConfigDto>> GetWeComChannelEffectiveConfigAsync(string instanceId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<WeComChannelEffectiveConfigDto>.ErrorResponse(500, "not configured"));

        public Task<ApiResponse<bool>> ClearWeComChannelOverrideAsync(string instanceId, CancellationToken cancellationToken = default)
            => Task.FromResult(ApiResponse<bool>.ErrorResponse(500, "not configured"));
    }



    private sealed class FakeTrainingService : ITrainingService
    {
        public Task<ApiResponse<TrainingStateDto>> GetTrainingStateAsync(string employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> SubmitTrainingDecisionAsync(string employeeId, TrainingDecisionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeEvaluationService : IEvaluationService
    {
        public Task<ApiResponse<EvaluationStateDto>> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EvaluationSandboxConversationStateDto>> GetEvaluationSandboxConversationAsync(string employeeId, string? since = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EvaluationSandboxConversationStateDto>> SendEvaluationSandboxMessageAsync(string employeeId, EvaluationSandboxMessageRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> SubmitAiEvaluationDecisionAsync(string employeeId, AiEvaluationDecisionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> SubmitOnboardingDecisionAsync(string employeeId, EvaluationOnboardingDecisionRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EvaluationSandboxConnectionResultDto>> GetSandboxConnectionAsync(string employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EvaluationVerdictSyncResultDto>> SyncVerdictAsync(string employeeId, EvaluationVerdictSyncRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EvaluationWorkspaceStatusDto>> GetWorkspaceStatusAsync(string employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

