using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/im/test-data")]
[ApiController]
[AllowAnonymous]
public sealed class ImWebhookReplayController(
    IImWebhookService imWebhookService,
    IImWebhookReplayContext replayContext) : ControllerBase
{
    [HttpPost("feishu/replay")]
    public async Task<IActionResult> ReplayFeishu(
        [FromBody] ImWebhookReplayRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId))
        {
            return BadRequest(ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "instanceId cannot be empty"));
        }

        if (string.IsNullOrWhiteSpace(request.RawPayload))
        {
            return BadRequest(ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "rawPayload cannot be empty"));
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Headers is not null)
        {
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value;
            }
        }

        replayContext.SkipOutboundSend = request.SkipOutboundSend;
        replayContext.UseMockKingCrew = request.UseMockKingCrew;
        replayContext.MockKingCrewReply = request.MockKingCrewReply;

        try
        {
            var response = await imWebhookService.HandleAsync(
                "feishu",
                request.InstanceId,
                request.RawPayload,
                headers,
                cancellationToken);

            return StatusCode(response.Code, response);
        }
        finally
        {
            replayContext.Reset();
        }
    }
}
