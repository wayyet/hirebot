using HireBot.Abstraction.Services.Hiring;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/hirings")]
[ApiController]
public sealed class HiringsController(IEmployeeHiringService employeeHiringService) : ControllerBase
{
    [HttpGet("{hireId}")]
    public async Task<IActionResult> GetHiringStatus(string hireId, CancellationToken cancellationToken = default)
    {
        var response = await employeeHiringService.GetHiringStatusAsync(hireId, cancellationToken);
        return StatusCode(response.Code, response);
    }
}
