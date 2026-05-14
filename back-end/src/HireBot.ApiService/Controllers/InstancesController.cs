using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

/// <summary>
/// 员工实例管理控制器
/// 提供员工实例的聊天消息管理和 IM 配置管理功能
/// </summary>
[Route("api/v1/instances")]
[ApiController]
[Authorize]
public sealed class InstancesController(
    IInstanceChatService instanceChatService,
    IEmployeeRuntimeService employeeRuntimeService) : ControllerBase
{
   

    


   

   

    

  

   

    /// <summary>
    /// 从个人分身创建私有分支。创建后状态为 hired，需经双阶段评估通过后才上岗。
    /// </summary>
    [HttpPost("{instanceId}/private-branch")]
    public async Task<IActionResult> CreatePrivateBranch(
        string instanceId,
        [FromBody] CreatePrivateBranchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<PrivateBranchResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await employeeRuntimeService.CreatePrivateBranchAsync(instanceId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 废弃私有分支。回滚五件套并原地恢复为个人分身。
    /// </summary>
    [HttpPost("{instanceId}/abandon-branch")]
    public async Task<IActionResult> AbandonPrivateBranch(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var response = await employeeRuntimeService.AbandonPrivateBranchAsync(instanceId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 构建模型校验错误响应
    /// </summary>
    /// <typeparam name="T">响应数据类型</typeparam>
    /// <returns>校验错误响应，如果校验通过则返回 null</returns>
    private IActionResult? BuildModelValidationError<T>()
    {
        if (ModelState.IsValid)
        {
            return null;
        }

        // 拼接所有错误消息
        var message = string.Join(
            "; ",
            ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .Where(errorMessage => !string.IsNullOrWhiteSpace(errorMessage)));

        // 构建错误响应
        var errorResponse = ApiResponse<T>.ErrorResponse(400, string.IsNullOrWhiteSpace(message) ? "请求参数校验失败" : message);
        return BadRequest(errorResponse);
    }
}
