using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.ApiService.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.Core.Tests;

public sealed class ImWebhookReplayControllerTests
{
    [Fact]
    public async Task ReplayFeishu_ReturnsWebhookResponseAndPassesRawBodyAndHeaders()
    {
        var replayContext = new FakeReplayContext();
        var webhook = new FakeWebhookService(replayContext)
        {
            Response = ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(new ImWebhookHandleResultDto("replied", "mock reply"))
        };
        var controller = new ImWebhookReplayController(webhook, replayContext);
        var request = new ImWebhookReplayRequestDto(
            "pc_1",
            """{"event":{"message":{"chat_type":"p2p"}}}""",
            new Dictionary<string, string>
            {
                ["X-Lark-Signature"] = "sig-1",
                ["X-Lark-Request-Timestamp"] = "1714440000"
            },
            SkipOutboundSend: true,
            UseMockKingCrew: true,
            MockKingCrewReply: "mock reply: hello");

        var result = await controller.ReplayFeishu(request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<ImWebhookHandleResultDto>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal("replied", response.Data!.Status);
        Assert.Equal("pc_1", webhook.InstanceId);
        Assert.Equal("feishu", webhook.Platform);
        Assert.Equal("""{"event":{"message":{"chat_type":"p2p"}}}""", webhook.Payload);
        Assert.True(webhook.SeenSkipOutboundSend);
        Assert.True(webhook.SeenUseMockKingCrew);
        Assert.Equal("mock reply: hello", webhook.SeenMockKingCrewReply);
        Assert.False(replayContext.SkipOutboundSend);
        Assert.False(replayContext.UseMockKingCrew);
        Assert.Null(replayContext.MockKingCrewReply);
        Assert.Contains("X-Lark-Signature", webhook.Headers!.Keys);
        Assert.Contains("X-Lark-Request-Timestamp", webhook.Headers.Keys);
    }

    private sealed class FakeWebhookService : IImWebhookService
    {
        private readonly IImWebhookReplayContext replayContext;

        public FakeWebhookService(IImWebhookReplayContext replayContext)
        {
            this.replayContext = replayContext;
        }

        public string? Platform { get; private set; }
        public string? InstanceId { get; private set; }
        public string? Payload { get; private set; }
        public IReadOnlyDictionary<string, string>? Headers { get; private set; }
        public bool SeenSkipOutboundSend { get; private set; }
        public bool SeenUseMockKingCrew { get; private set; }
        public string? SeenMockKingCrewReply { get; private set; }

        public ApiResponse<ImWebhookHandleResultDto> Response { get; init; } =
            ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(500, "not configured");

        public Task<ApiResponse<ImWebhookHandleResultDto>> HandleAsync(
            string platform,
            string instanceId,
            string payload,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken = default)
        {
            Platform = platform;
            InstanceId = instanceId;
            Payload = payload;
            Headers = headers;
            SeenSkipOutboundSend = replayContext.SkipOutboundSend;
            SeenUseMockKingCrew = replayContext.UseMockKingCrew;
            SeenMockKingCrewReply = replayContext.MockKingCrewReply;
            return Task.FromResult(Response);
        }
    }

    private sealed class FakeReplayContext : IImWebhookReplayContext
    {
        public bool SkipOutboundSend { get; set; }

        public bool UseMockKingCrew { get; set; }

        public string? MockKingCrewReply { get; set; }

        public void Reset()
        {
            SkipOutboundSend = false;
            UseMockKingCrew = false;
            MockKingCrewReply = null;
        }
    }
}
