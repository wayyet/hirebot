using HireBot.Abstraction.Services.SkillCatalog;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/skills")]
[ApiController]
public sealed class SkillsController(ISkillCatalogService skillCatalogService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSkills(
        [FromQuery] string? q,
        [FromQuery] string? level,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        var response = await skillCatalogService.GetSkillsAsync(q, level, status, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{skillId}")]
    public async Task<IActionResult> GetSkill(string skillId, CancellationToken cancellationToken = default)
    {
        var response = await skillCatalogService.GetSkillAsync(skillId, cancellationToken);
        return StatusCode(response.Code, response);
    }
}
