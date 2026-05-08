using HireBot.Abstraction;
using HireBot.Abstraction.Models.Collaboration;
using HireBot.Abstraction.Services.Collaboration;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/collaboration/groups")]
[ApiController]
public sealed class CollaborationGroupsController(ICollaborationService collaborationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetGroups([FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var response = await collaborationService.GetGroupsAsync(includeArchived, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpGet("{groupId}")]
    public async Task<IActionResult> GetGroup(string groupId, CancellationToken cancellationToken = default)
    {
        var response = await collaborationService.GetGroupAsync(groupId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    [HttpPost("{groupId}/archive")]
    public async Task<IActionResult> SetArchived(
        string groupId,
        [FromBody] ArchiveCollaborationGroupRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await collaborationService.SetArchivedAsync(groupId, request.Archived, cancellationToken);
        return StatusCode(response.Code, response);
    }
}
