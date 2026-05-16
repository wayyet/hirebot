using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeTemplate;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/employee-templates")]
[ApiController]
public sealed class EmployeeTemplatesController(
    IEmployeeTemplateService employeeTemplateService,
    IEmployeeHiringService employeeHiringService,
    IEmployeeRuntimeService employeeRuntimeService) : ControllerBase
{
    [HttpGet("{templateId}")]
    public async Task<IActionResult> GetTemplateDetail(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeTemplateService.GetTemplateDetailAsync(templateId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{templateId}/hire")]
    public async Task<IActionResult> HireTemplate(
        string templateId,
        [FromBody] HireTemplateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var errorMessages = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message));

            var message = string.Join("; ", errorMessages);
            var badRequestResponse = ApiResponse<HireTemplateResultDto>.ErrorResponse(
                400,
                string.IsNullOrWhiteSpace(message) ? "请求参数校验失败" : message);

            return BadRequest(badRequestResponse);
        }

        var response = await employeeHiringService.HireAsync(templateId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{templateId}/fixture-hire")]
    public async Task<IActionResult> HireFromFixtureTemplate(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.HireFromFixtureTemplateAsync(templateId, cancellationToken);
        return StatusCode(response.Code, response);
    }
}
