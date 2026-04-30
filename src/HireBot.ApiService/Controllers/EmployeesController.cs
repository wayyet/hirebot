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

    [HttpGet("{employeeId}")]
    public async Task<IActionResult> GetEmployee(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.GetEmployeeAsync(employeeId, cancellationToken);
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

    [HttpGet("{employeeId}/evaluation/state")]
    public async Task<IActionResult> GetEvaluationState(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.GetEvaluationStateAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/evaluation/sandbox/conversation")]
    public async Task<IActionResult> GetEvaluationSandboxConversation(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.GetEvaluationSandboxConversationAsync(employeeId, cancellationToken);
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

    [HttpGet("{employeeId}/evaluation/tools/testcases")]
    public async Task<IActionResult> FetchEvaluationTestcases(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.FetchTestcasesAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{employeeId}/evaluation/tools/ontology")]
    public async Task<IActionResult> QueryEvaluationOntology(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var response = await evaluationService.QueryOntologyAsync(employeeId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/target/bootstrap")]
    public async Task<IActionResult> BootstrapTargetSandbox(
        string employeeId,
        [FromBody] EvaluationTargetBootstrapRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EvaluationTargetBootstrapResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.BootstrapTargetSandboxAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/tools/target-execute")]
    public async Task<IActionResult> ExecuteTargetSandbox(
        string employeeId,
        [FromBody] EvaluationTargetExecuteRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EvaluationTargetExecuteResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.ExecuteTargetAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/tools/trace-read")]
    public async Task<IActionResult> ReadTargetTrace(
        string employeeId,
        [FromBody] EvaluationTraceReadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EvaluationTraceReadResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.ReadTraceAsync(employeeId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{employeeId}/evaluation/tools/report")]
    public async Task<IActionResult> UpsertEvaluationReport(
        string employeeId,
        [FromBody] EvaluationReportUpsertRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<EvaluationReportUpsertResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await evaluationService.UpsertReportAsync(employeeId, request, cancellationToken);
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
