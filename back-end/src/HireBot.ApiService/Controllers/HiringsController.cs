using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/hirings")]
[ApiController]
public sealed class HiringsController(
    IEmployeeHiringService employeeHiringService,
    IHiringTodoService hiringTodoService) : ControllerBase
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

    [HttpPost("{hireId}/conversation/reset")]
    public async Task<IActionResult> ResetConversation(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.ResetConversationAsync(hireId, cancellationToken);
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

    /// <summary>
    /// 前端从沙箱网关直接下载产物包后上传至此接口，跳过后端对 KingCrab 的依赖，完成数字员工创建。
    /// </summary>
    [HttpPost("{hireId}/import-package")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportPackage(
        string hireId,
        IFormFile? packageFile,
        CancellationToken cancellationToken = default)
    {
        if (packageFile is null || packageFile.Length == 0)
        {
            var badReq = ApiResponse<object>.ErrorResponse(400, "必须上传产物包文件");
            return BadRequest(badReq);
        }

        await using var stream = packageFile.OpenReadStream();
        var response = await employeeHiringService.ImportPackageAsync(hireId, stream, packageFile.FileName, cancellationToken);
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

    /// <summary>获取前端对话状态缓存（刷新页面后用于恢复对话历史）。</summary>
    [HttpGet("{hireId}/conversation/cache")]
    public async Task<IActionResult> GetConversationCache(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetConversationCacheAsync(hireId, cancellationToken);
        return Ok(response);
    }

    /// <summary>保存前端对话状态缓存（messages + stageOverrides）。</summary>
    [HttpPut("{hireId}/conversation/cache")]
    public async Task<IActionResult> SaveConversationCache(
        string hireId,
        [FromBody] JsonElement cache,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.SaveConversationCacheAsync(hireId, cache, cancellationToken);
        return Ok(response);
    }
    /// <summary>获取该雇佣流程的所有 TODO 事项（供前端 TODO 面板初始化加载）。</summary>
    [HttpGet("{hireId}/todos")]
    public async Task<IActionResult> GetTodos(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await hiringTodoService.GetTodosByHireIdAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>用户确认或撤销一个 TODO 事项。</summary>
    [HttpPatch("{hireId}/todos/{handoffId}")]
    public async Task<IActionResult> UpdateTodoStatus(
        string hireId,
        string handoffId,
        [FromBody] UpdateTodoStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await hiringTodoService.UpdateTodoStatusAsync(hireId, handoffId, request.Status, cancellationToken);
        return StatusCode(response.Code, response);
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
