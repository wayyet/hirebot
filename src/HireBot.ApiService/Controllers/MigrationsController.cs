using HireBot.Abstraction;
using HireBot.Abstraction.Models.Migration;
using HireBot.Abstraction.Services.EmployeeRuntime;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/migrations")]
[ApiController]
public sealed class MigrationsController(IEmployeeRuntimeService employeeRuntimeService) : ControllerBase
{
    [HttpPost("local-state")]
    public async Task<IActionResult> MigrateLocalState(
        [FromBody] LocalStateMigrationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<LocalStateMigrationResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeRuntimeService.MigrateLocalStateAsync(request, cancellationToken);
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
