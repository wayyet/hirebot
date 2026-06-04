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

    /// <summary>同步对话轮次，解析 AI 回复中的结构化数据标签并保存。</summary>
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

    /// <summary>获取已收集的结构化数据。</summary>
    [HttpGet("{hireId}/structured-data")]
    public async Task<IActionResult> GetStructuredData(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetStructuredDataAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>保存运行时状态（阶段覆盖配置 + 下游运行记录，统一接口）。</summary>
    [HttpPut("{hireId}/runtime-state")]
    public async Task<IActionResult> SaveRuntimeState(
        string hireId,
        [FromBody] SaveRuntimeStateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.SaveRuntimeStateAsync(hireId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>获取运行时状态（阶段覆盖配置 + 下游运行记录）。</summary>
    [HttpGet("{hireId}/runtime-state")]
    public async Task<IActionResult> GetRuntimeState(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetRuntimeStateAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 前端从沙箱网关直接下载产物包后上传至此接口,跳过后端对 KingCrab 的依赖，完成数字员工创建。
    /// </summary>
    /// <param name="hireId">雇佣会话 ID。</param>
    /// <param name="packageFile">沙箱生成的产物 zip（multipart 文件字段）。</param>
    /// <param name="skillIds">可选：用户在 TODO 面板关联的 store skill UUID 列表（multipart 重复字段），后端会从 ncrew-builder 下载并合并。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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

    /// <summary>
    /// 接收前端直传的模板包 ZIP，上传到雇佣沙箱工作区。
    /// 返回沙箱内文件路径和可嵌入 WebSocket 消息的 [FILE_URL:...] 标记。
    /// </summary>
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
