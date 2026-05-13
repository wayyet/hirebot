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
    [Fact]
    public async Task CreatePersonalClone_ReturnsServiceStatusAndPayload()
    {
        var employeeRuntime = new FakeEmployeeRuntimeService
        {
            CreatePersonalCloneResponse = ApiResponse<EmployeeDetailDto>.SuccessResponse(BuildEmployee("pc_1", "personal_clone", "live", "dept_1"))
        };
        var controller = new EmployeesController(employeeRuntime, new FakeTrainingService(), new FakeEvaluationService());
        var request = new CreatePersonalCloneRequestDto("我的销售分身", null, "desc");

        var result = await controller.CreatePersonalClone("dept_1", request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<EmployeeDetailDto>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal("pc_1", response.Data!.EmployeeId);
        Assert.Equal("dept_1", employeeRuntime.CreatePersonalCloneSourceId);
        Assert.Same(request, employeeRuntime.CreatePersonalCloneRequest);
    }

    [Fact]
    public async Task CreatePersonalClone_WhenModelStateInvalid_ReturnsBadRequestAndDoesNotCallService()
    {
        var employeeRuntime = new FakeEmployeeRuntimeService();
        var controller = new EmployeesController(employeeRuntime, new FakeTrainingService(), new FakeEvaluationService());
        controller.ModelState.AddModelError("displayName", "displayName is required");

        var result = await controller.CreatePersonalClone("dept_1", new CreatePersonalCloneRequestDto("", null, null));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ApiResponse<EmployeeDetailDto>>(badRequest.Value);
        Assert.False(response.Success);
        Assert.Equal(400, response.Code);
        Assert.Null(employeeRuntime.CreatePersonalCloneSourceId);
    }

    [Fact]
    public async Task GetInAppChatMessages_ReturnsTimelineFromService()
    {
        var chat = new FakeInstanceChatService
        {
            GetResponse = ApiResponse<InstanceChatTimelineDto>.SuccessResponse(
                new InstanceChatTimelineDto("pc_1", "conv_1", [new InstanceChatMessageDto("msg_1", "user", "hi", DateTimeOffset.UtcNow)]))
        };
        var controller = new InstancesController(chat, new FakeEmployeeRuntimeService());

        var result = await controller.GetInAppChatMessages("pc_1");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<InstanceChatTimelineDto>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal("pc_1", response.Data!.InstanceId);
        Assert.Equal("pc_1", chat.GetInstanceId);
    }

    [Fact]
    public async Task SendInAppChatMessage_ReturnsAssistantMessageFromService()
    {
        var chat = new FakeInstanceChatService
        {
            SendResponse = ApiResponse<InstanceChatResultDto>.SuccessResponse(
                new InstanceChatResultDto("pc_1", "conv_1", new InstanceChatMessageDto("msg_2", "assistant", "reply", DateTimeOffset.UtcNow)))
        };
        var controller = new InstancesController(chat, new FakeEmployeeRuntimeService());
        var request = new SendInstanceChatMessageRequestDto("hello");

        var result = await controller.SendInAppChatMessage("pc_1", request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<InstanceChatResultDto>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal("reply", response.Data!.AssistantMessage.Content);
        Assert.Equal("pc_1", chat.SendInstanceId);
        Assert.Same(request, chat.SendRequest);
    }

    [Fact]
    public async Task SendInAppChatMessage_WhenModelStateInvalid_ReturnsBadRequestAndDoesNotCallService()
    {
        var chat = new FakeInstanceChatService();
        var controller = new InstancesController(chat, new FakeEmployeeRuntimeService());
        controller.ModelState.AddModelError("content", "content is required");

        var result = await controller.SendInAppChatMessage("pc_1", new SendInstanceChatMessageRequestDto(""));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ApiResponse<InstanceChatResultDto>>(badRequest.Value);
        Assert.False(response.Success);
        Assert.Equal(400, response.Code);
        Assert.Null(chat.SendInstanceId);
    }

    [Fact]
    public async Task ClearInAppChatMessages_ReturnsServiceResult()
    {
        var chat = new FakeInstanceChatService
        {
            ClearResponse = ApiResponse<bool>.SuccessResponse(true, "对话已清空")
        };
        var controller = new InstancesController(chat, new FakeEmployeeRuntimeService());

        var result = await controller.ClearInAppChatMessages("pc_1");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<bool>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.True(response.Data);
        Assert.Equal("pc_1", chat.ClearInstanceId);
    }

    [Fact]
    public async Task GetEffectiveImConfig_ForFeishu_ReturnsServiceResult()
    {
        var chat = new FakeInstanceChatService
        {
            GetEffectiveFeishuResponse = ApiResponse<FeishuChannelEffectiveConfigDto>.SuccessResponse(
                new FeishuChannelEffectiveConfigDto(
                    true,
                    "cli_test",
                    "env:FEISHU_APP_ID",
                    "secret",
                    "env:FEISHU_APP_SECRET",
                    "open",
                    ["ou_1"],
                    ["oc_1"],
                    4096,
                    false,
                    true))
        };
        var controller = new InstancesController(chat, new FakeEmployeeRuntimeService());

        var result = await controller.GetEffectiveImConfig("pc_1", "feishu");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<FeishuChannelEffectiveConfigDto>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal("cli_test", response.Data!.AppId);
        Assert.Equal("pc_1", chat.GetEffectiveFeishuInstanceId);
    }

    [Fact]
    public async Task UpsertDingTalkImConfig_ReturnsServiceResult()
    {
        var chat = new FakeInstanceChatService
        {
            UpdateDingTalkResponse = ApiResponse<ImConfigResultDto>.SuccessResponse(
                new ImConfigResultDto("dingtalk", "url_callback", "active", "钉钉配置已更新", DateTimeOffset.UtcNow))
        };
        var controller = new InstancesController(chat, new FakeEmployeeRuntimeService());
        var request = new DingTalkChannelConfig
        {
            Enabled = true,
            AppId = "ding_app",
            AppKey = "ding_key",
            AppSecret = "ding_secret"
          
        };

        var result = await controller.UpsertDingTalkImConfig("pc_1", request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<ImConfigResultDto>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal("dingtalk", response.Data!.Platform);
        Assert.Equal("pc_1", chat.UpdateDingTalkInstanceId);
        Assert.Same(request, chat.UpdateDingTalkRequest);
    }

   
    

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

    private sealed class FakeEmployeeRuntimeService : IEmployeeRuntimeService
    {
        public string? CreatePersonalCloneSourceId { get; private set; }
        public CreatePersonalCloneRequestDto? CreatePersonalCloneRequest { get; private set; }

        public ApiResponse<EmployeeDetailDto> CreatePersonalCloneResponse { get; init; } =
            ApiResponse<EmployeeDetailDto>.ErrorResponse(500, "not configured");

        public Task<ApiResponse<EmployeeDetailDto>> CreatePersonalCloneAsync(string sourceEmployeeId, CreatePersonalCloneRequestDto request, CancellationToken cancellationToken = default)
        {
            CreatePersonalCloneSourceId = sourceEmployeeId;
            CreatePersonalCloneRequest = request;
            return Task.FromResult(CreatePersonalCloneResponse);
        }

        public Task<ApiResponse<IReadOnlyList<EmployeeSummaryDto>>> GetEmployeesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> GetEmployeeAsync(string employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<string>> GetRuntimeSandboxGatewayEndpointAsync(string instanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<ImportFixtureInstancesResultDto>> ImportFixtureInstancesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<FixtureTemplateHireResultDto>> HireFromFixtureTemplateAsync(string templateId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> UpdateLifecycleAsync(string employeeId, UpdateEmployeeLifecycleRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> RehireAsync(string employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> UpdateCapabilitiesAsync(string employeeId, UpdateEmployeeCapabilitiesRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> CompletePendingActionAsync(string employeeId, string actionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> CreateFromHireAsync(CreateEmployeeFromHireRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<PrivateBranchResultDto>> CreatePrivateBranchAsync(string sourceInstanceId, CreatePrivateBranchRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<EmployeeDetailDto>> AbandonPrivateBranchAsync(string branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResponse<LocalStateMigrationResultDto>> MigrateLocalStateAsync(LocalStateMigrationRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

