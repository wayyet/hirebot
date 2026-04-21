using HireBot.Abstraction;
using HireBot.Abstraction.Models.User;
using HireBot.Abstraction.Services.User;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class UserController(IUserService userService) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var response = await userService.GetUserByIdAsync(id);
        if (!response.Success)
        {
            return StatusCode(response.Code, response);
        }
        return Ok(response);
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUsers()
    {
        var response = await userService.GetAllUsersAsync();
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto createUserDto)
    {
        var response = await userService.CreateUserAsync(createUserDto);
        if (!response.Success)
        {
            return StatusCode(response.Code, response);
        }
        return CreatedAtAction(nameof(GetUser), new { id = response.Data?.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto updateUserDto)
    {
        var response = await userService.UpdateUserAsync(id, updateUserDto);
        if (!response.Success)
        {
            return StatusCode(response.Code, response);
        }
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var response = await userService.DeleteUserAsync(id);
        if (!response.Success)
        {
            return StatusCode(response.Code, response);
        }
        return Ok(response);
    }
}
