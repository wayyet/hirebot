using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet("{hireId}/external-config")]
    public async Task<IActionResult> GetExternalSystemConfig(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetExternalSystemConfigAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPut("{hireId}/external-config")]
    public async Task<IActionResult> SaveExternalSystemConfig(
        string hireId,
        [FromBody] HiringExternalSystemConfigDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringExternalSystemConfigDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.SaveExternalSystemConfigAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/skill-link-config")]
    public async Task<IActionResult> GetSkillLinkConfig(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetSkillLinkConfigAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPut("{hireId}/skill-link-config")]
    public async Task<IActionResult> SaveSkillLinkConfig(
        string hireId,
        [FromBody] HiringSkillLinkConfigDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringSkillLinkConfigDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.SaveSkillLinkConfigAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/conversation/sync")]
    public async Task<IActionResult> SyncConversationTurn(
        string hireId,
        [FromBody] HiringConversationSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<HiringConversationSyncResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeHiringService.SyncConversationTurnAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/structured-data")]
    public async Task<IActionResult> GetStructuredData(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetStructuredDataAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPut("{hireId}/runtime-state/{stage}")]
    public async Task<IActionResult> SaveRuntimeStateByStage(
        string hireId,
        string stage,
        [FromBody] SaveRuntimeStateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.SaveRuntimeStateByStageAsync(hireId, stage, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{hireId}/runtime-state/{stage}")]
    public async Task<IActionResult> GetRuntimeStateByStage(
        string hireId,
        string stage,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetRuntimeStateByStageAsync(hireId, stage, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{hireId}/import-package")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportPackage(
        string hireId,
        IFormFile? packageFile,
        [FromForm(Name = "skillIds")] string[]? skillIds,
        CancellationToken cancellationToken = default)
    {
        if (packageFile is null || packageFile.Length == 0)
        {
            var badReq = ApiResponse<object>.ErrorResponse(400, "必须上传产物包文件");
            return BadRequest(badReq);
        }

        var linkedStoreSkillIds = skillIds is null
            ? null
            : skillIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        await using var stream = packageFile.OpenReadStream();
        var response = await employeeHiringService.ImportPackageAsync(
            hireId,
            stream,
            packageFile.FileName,
            linkedStoreSkillIds,
            cancellationToken);
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

    [HttpGet("{hireId}/todos")]
    public async Task<IActionResult> GetTodos(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await hiringTodoService.GetTodosByHireIdAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

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

    [HttpPost("{hireId}/template-package")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(250_000_000)]
    public async Task<IActionResult> UploadTemplatePackage(
        string hireId,
        IFormFile? templatePackage,
        CancellationToken cancellationToken = default)
    {
        if (templatePackage is null || templatePackage.Length == 0)
        {
            var badReq = ApiResponse<object>.ErrorResponse(400, "必须上传模板包文件");
            return BadRequest(badReq);
        }

        var fileName = templatePackage.FileName ?? "template.zip";
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var badReq = ApiResponse<object>.ErrorResponse(400, "仅支持 .zip 格式的模板包");
            return BadRequest(badReq);
        }

        await using var stream = templatePackage.OpenReadStream();
        var response = await employeeHiringService.UploadTemplatePackageFromClientAsync(
            hireId, stream, fileName, cancellationToken);
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
