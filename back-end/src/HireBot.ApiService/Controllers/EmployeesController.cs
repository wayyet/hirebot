using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;
using HireBot.Abstraction.Models.Training;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/employees")]
[ApiController]
[Authorize]
public sealed class EmployeesController(
    IEmployeeRuntimeService employeeRuntimeService,
    ITrainingService trainingService,
    IEvaluationService evaluationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEmployees(CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.GetEmployeesAsync(cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("department")]
    public async Task<IActionResult> GetDepartmentEmployees(CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.GetDepartmentEmployeesAsync(cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}")]
    public async Task<IActionResult> GetEmployee(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.GetEmployeeAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/sandbox/gateway-endpoint")]
    public async Task<IActionResult> GetSandboxGatewayEndpoint(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.GetRuntimeSandboxGatewayEndpointAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/lifecycle")]
    public async Task<IActionResult> UpdateLifecycle(
        string employeeId,
        [FromBody] UpdateEmployeeLifecycleRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EmployeeDetailDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeRuntimeService.UpdateLifecycleAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/rehire")]
    public async Task<IActionResult> Rehire(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.RehireAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPut("{employeeId}/capabilities")]
    public async Task<IActionResult> UpdateCapabilities(
        string employeeId,
        [FromBody] UpdateEmployeeCapabilitiesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EmployeeDetailDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeRuntimeService.UpdateCapabilitiesAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/pending-actions/{actionId}/complete")]
    public async Task<IActionResult> CompletePendingAction(
        string employeeId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.CompletePendingActionAsync(employeeId, actionId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/personal-clones")]
    public async Task<IActionResult> CreatePersonalClone(
        string employeeId,
        [FromBody] CreatePersonalCloneRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EmployeeDetailDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeRuntimeService.CreatePersonalCloneAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 上传模板包并直接从模板创建已上岗员工，跳过雇佣沟通、评估、实习等环节。
    /// </summary>
    [HttpPost("quick-create")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(250_000_000)]
    public async Task<IActionResult> QuickCreateFromTemplate(
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
        var response = await employeeRuntimeService.QuickCreateFromTemplateAsync(stream, fileName, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 删除数字员工及其全部关联资源。
    /// </summary>
    [HttpDelete("{employeeId}")]
    public async Task<IActionResult> DeleteEmployee(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.DeleteEmployeeAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/training/state")]
    public async Task<IActionResult> GetTrainingState(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await trainingService.GetTrainingStateAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/training/decision")]
    public async Task<IActionResult> SubmitTrainingDecision(
        string employeeId,
        [FromBody] TrainingDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EmployeeDetailDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await trainingService.SubmitTrainingDecisionAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/evaluation/workspace-status")]
    public async Task<IActionResult> GetEvaluationWorkspaceStatus(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.GetWorkspaceStatusAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/evaluation/state")]
    public async Task<IActionResult> GetEvaluationState(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.GetEvaluationStateAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/evaluation/sandbox/conversation")]
    public async Task<IActionResult> GetEvaluationSandboxConversation(
        string employeeId,
        [FromQuery(Name = "since")] string? since = null,
        CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.GetEvaluationSandboxConversationAsync(employeeId, since, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/sandbox/messages")]
    public async Task<IActionResult> SendEvaluationSandboxMessage(
        string employeeId,
        [FromBody] EvaluationSandboxMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EvaluationSandboxConversationStateDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.SendEvaluationSandboxMessageAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/ai-decision")]
    public async Task<IActionResult> SubmitAiEvaluationDecision(
        string employeeId,
        [FromBody] AiEvaluationDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EmployeeDetailDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.SubmitAiEvaluationDecisionAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/onboarding-decision")]
    public async Task<IActionResult> SubmitOnboardingDecision(
        string employeeId,
        [FromBody] EvaluationOnboardingDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EmployeeDetailDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.SubmitOnboardingDecisionAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/evaluation/sandbox-connection")]
    public async Task<IActionResult> GetSandboxConnection(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.GetSandboxConnectionAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/sync-verdict")]
    public async Task<IActionResult> SyncVerdict(
        string employeeId,
        [FromBody] EvaluationVerdictSyncRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EvaluationVerdictSyncResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.SyncVerdictAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
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
