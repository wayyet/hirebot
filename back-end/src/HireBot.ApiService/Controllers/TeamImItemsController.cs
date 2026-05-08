using HireBot.Abstraction.Models.Team;
using HireBot.Abstraction.Services.Team;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/team/im-items")]
[ApiController]
public sealed class TeamImItemsController(ITeamImService teamImService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetItems([FromQuery] TeamImQueryDto query, CancellationToken cancellationToken = default)
    {
        var response = await teamImService.GetItemsAsync(query, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{itemId}/confirm")]
    public async Task<IActionResult> Confirm(
        string itemId,
        [FromBody] ConfirmTeamImItemRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var response = await teamImService.ConfirmItemAsync(itemId, request ?? new ConfirmTeamImItemRequestDto(), cancellationToken);
        return StatusCode(response.Code, response);
    }
}
