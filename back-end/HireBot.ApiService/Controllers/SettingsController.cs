using System.Security.Claims;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

/// <summary>
/// 用户个人设置相关接口，目前主要提供雇佣沙箱的查询与管理能力。
/// </summary>
[Route("api/v1/settings")]
[ApiController]
public sealed class SettingsController(ISandboxService sandboxService) : ControllerBase
{
    /// <summary>
    /// 获取当前登录用户的所有活跃雇佣沙箱列表。
    /// </summary>
    [HttpGet("sandboxes")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SandboxInstanceDto>>), 200)]
    public async Task<IActionResult> ListSandboxes(CancellationToken cancellationToken = default)
    {
        var ownerSubject = ResolveOwner();
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return Unauthorized(ApiResponse<object>.ErrorResponse(401, "无法解析当前用户身份"));
        }

        var response = await sandboxService.ListByOwnerAsync(ownerSubject, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 删除当前登录用户的指定沙箱。
    /// </summary>
    /// <param name="sandboxId">沙箱 ID</param>
    [HttpDelete("sandboxes/{sandboxId}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> DeleteSandbox(string sandboxId, CancellationToken cancellationToken = default)
    {
        var ownerSubject = ResolveOwner();
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return Unauthorized(ApiResponse<object>.ErrorResponse(401, "无法解析当前用户身份"));
        }

        var response = await sandboxService.DeleteForOwnerAsync(sandboxId, ownerSubject, cancellationToken);
        return StatusCode(response.Code, response);
    }

    // 从 JWT claims 解析当前用户主体标识（优先 sub，回退到 X-HireBot-Owner header）
    private string? ResolveOwner()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(sub))
        {
            return sub.Trim();
        }

        var ownerHeader = Request.Headers["X-HireBot-Owner"].ToString();
        return string.IsNullOrWhiteSpace(ownerHeader) ? null : ownerHeader.Trim();
    }
}
