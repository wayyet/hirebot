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
    ITemplateSkillRecommendationService templateSkillRecommendationService,
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

    /// <summary>
    /// 根据当前雇佣模板从构建端 Store 推荐可关联技能。
    /// </summary>
    /// <param name="templateId">构建端模板 ID。</param>
    /// <param name="limit">返回数量，默认 5，最大 10。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    [HttpGet("{templateId}/recommended-skills")]
    public async Task<IActionResult> GetRecommendedSkills(
        string templateId,
        [FromQuery] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var response = await templateSkillRecommendationService.GetRecommendedSkillsAsync(
            templateId,
            limit,
            cancellationToken);
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
