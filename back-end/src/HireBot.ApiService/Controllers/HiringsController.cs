using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/hirings")]
[ApiController]
public sealed class HiringsController(IEmployeeHiringService employeeHiringService) : ControllerBase
{
    [HttpGet("{hireId}")]
    public async Task<IActionResult> GetHiringStatus(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetHiringStatusAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/conversation/start")]
    public async Task<IActionResult> StartConversation(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.StartConversationAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/conversation/pause")]
    public async Task<IActionResult> PauseConversation(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.PauseConversationAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/conversation/resume")]
    public async Task<IActionResult> ResumeConversation(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.ResumeConversationAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/conversation/messages")]
    public async Task<IActionResult> SendConversationMessage(
        string hireId,
        [FromBody] HiringConversationMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringConversationResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.SendConversationMessageAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/conversation/sync")]
    public async Task<IActionResult> SyncConversationTurn(
        string hireId,
        [FromBody] HiringConversationSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringConversationResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.SyncConversationTurnAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/conversation/messages")]
    public async Task<IActionResult> GetConversationTimeline(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetConversationTimelineAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/stage-preview")]
    public async Task<IActionResult> GetStagePreview(
        string hireId,
        [FromQuery] string? stage = null,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetStagePreviewAsync(hireId, stage, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/audit-decisions")]
    public async Task<IActionResult> SubmitAuditDecision(
        string hireId,
        [FromBody] HiringAuditDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringAuditDecisionResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.SubmitAuditDecisionAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/audit-logs")]
    public async Task<IActionResult> GetAuditLogs(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetAuditLogsAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/finalize")]
    public async Task<IActionResult> FinalizeHiring(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.FinalizeAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/workflow")]
    public async Task<IActionResult> GetWorkflowState(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetWorkflowStateAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/credential-bindings")]
    public async Task<IActionResult> UpsertCredentialBinding(
        string hireId,
        [FromBody] HiringCredentialBindingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringWorkflowStateDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.UpsertCredentialBindingAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPut("{hireId}/config-files/{configKey}")]
    public async Task<IActionResult> UpdateConfigFile(
        string hireId,
        string configKey,
        [FromBody] HiringConfigFileUpdateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringWorkflowStateDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.UpdateConfigFileAsync(hireId, configKey, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/artifacts/download")]
    public async Task<IActionResult> DownloadArtifacts(string hireId, CancellationToken cancellationToken = default)
    {
        var result = await employeeHiringService.BuildArtifactDownloadAsync(hireId, cancellationToken);
        return BuildDownloadResponse(result);
    }

    [HttpGet("{hireId}/artifacts/{*artifactName}")]
    public async Task<IActionResult> DownloadArtifactFile(
        string hireId,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        var result = await employeeHiringService.BuildArtifactFileDownloadAsync(hireId, artifactName, cancellationToken);
        return BuildDownloadResponse(result);
    }

    private IActionResult BuildDownloadResponse(HiringArtifactDownloadResult result)
    {
        if (!result.Found)
        {
            var error = ApiResponse<object>.ErrorResponse(result.Code, result.Message);
            return StatusCode(result.Code, error);
        }

        if (result.Content is null || string.IsNullOrWhiteSpace(result.ContentType) || string.IsNullOrWhiteSpace(result.FileName))
        {
            var error = ApiResponse<object>.ErrorResponse(500, "交付物下载结果异常");
            return StatusCode(500, error);
        }

        return File(result.Content, result.ContentType, result.FileName);
    }

    private IActionResult? BuildModelValidationError<T>()
    {
        if (ModelState.IsValid)
        {
            return null;
        }

        var message = string.Join(
            "; ",
            ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .Where(errorMessage => !string.IsNullOrWhiteSpace(errorMessage)));

        var errorResponse = ApiResponse<T>.ErrorResponse(400, string.IsNullOrWhiteSpace(message) ? "请求参数校验失败" : message);
        return BadRequest(errorResponse);
    }
}
