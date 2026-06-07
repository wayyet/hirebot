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
    /// 更新或创建实例的 IM 配置
    /// </summary>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="platform">IM 平台名称</param>
    /// <param name="request">IM 配置请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>配置结果</returns>
    [HttpPut("{instanceId}/im-config/{platform}")]
    public async Task<IActionResult> UpsertImConfig(
        string instanceId,
        string platform,
        [FromBody] ImConfigRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // 参数校验
        var invalidResponse = BuildModelValidationError<ImConfigResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        if (string.Equals(platform, "feishu", StringComparison.OrdinalIgnoreCase))
        {
            var feishuResponse = await instanceChatService.UpdateFeishuChannelConfigAsync(instanceId, request, cancellationToken);
            return StatusCode(feishuResponse.Code, feishuResponse);
        }

        if (string.Equals(platform, "wecom", StringComparison.OrdinalIgnoreCase))
        {
            var wecomResponse = await instanceChatService.UpdateWeComChannelConfigAsync(instanceId, request, cancellationToken);
            return StatusCode(wecomResponse.Code, wecomResponse);
        }

        return BadRequest(ApiResponse<ImConfigResultDto>.ErrorResponse(400, $"不支持的平台 '{platform}'，仅支持 feishu / dingtalk / wecom"));
    }

    /// <summary>
    /// 更新钉钉频道的配置。
    /// </summary>
    [HttpPut("{instanceId}/im-config/dingtalk")]
    public async Task<IActionResult> UpsertDingTalkImConfig(
        string instanceId,
        [FromBody] DingTalkChannelConfig request,
        CancellationToken cancellationToken = default)
    {
        var invalidResponse = BuildModelValidationError<ImConfigResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        var response = await instanceChatService.UpdateDingTalkChannelConfigAsync(instanceId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 获取实例的所有 IM 配置列表
    /// </summary>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>IM 配置列表</returns>
    /// <summary>
    /// 获取实例指定 IM 平台的当前生效配置。
    /// </summary>
    [HttpGet("{instanceId}/im-config/{platform}/effective")]
    public async Task<IActionResult> GetEffectiveImConfig(
        string instanceId,
        string platform,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(platform, "feishu", StringComparison.OrdinalIgnoreCase))
        {
            var feishuResponse = await instanceChatService.GetFeishuChannelEffectiveConfigAsync(instanceId, cancellationToken);
            return StatusCode(feishuResponse.Code, feishuResponse);
        }

        if (string.Equals(platform, "dingtalk", StringComparison.OrdinalIgnoreCase))
        {
            var feishuResponse = await instanceChatService.GetDingTalkChannelEffectiveConfigAsync(instanceId, cancellationToken);
            return StatusCode(feishuResponse.Code, feishuResponse);
        }

        if (string.Equals(platform, "wecom", StringComparison.OrdinalIgnoreCase))
        {
            var wecomResponse = await instanceChatService.GetWeComChannelEffectiveConfigAsync(instanceId, cancellationToken);
            return StatusCode(wecomResponse.Code, wecomResponse);
        }

        var response = ApiResponse<FeishuChannelEffectiveConfigDto>.ErrorResponse(
            404,
            $"Unknown or unsupported platform '{platform}'.");
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 删除实例的指定 IM 配置
    /// </summary>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="platform">IM 平台名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{instanceId}/im-config/{platform}")]
    public async Task<IActionResult> DeleteImConfig(
        string instanceId,
        string platform,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(platform, "feishu", StringComparison.OrdinalIgnoreCase))
        {
            var feishuResponse = await instanceChatService.ClearFeishuChannelOverrideAsync(instanceId, cancellationToken);
            return StatusCode(feishuResponse.Code, feishuResponse);
        }

        if (string.Equals(platform, "dingtalk", StringComparison.OrdinalIgnoreCase))
        {
            var dingtalkResponse = await instanceChatService.ClearDingTalkChannelOverrideAsync(instanceId, cancellationToken);
            return StatusCode(dingtalkResponse.Code, dingtalkResponse);
        }

        if (string.Equals(platform, "wecom", StringComparison.OrdinalIgnoreCase))
        {
            var wecomResponse = await instanceChatService.ClearWeComChannelOverrideAsync(instanceId, cancellationToken);
            return StatusCode(wecomResponse.Code, wecomResponse);
        }

        return BadRequest(ApiResponse<bool>.ErrorResponse(400, $"不支持的平台 '{platform}'，仅支持 feishu / dingtalk / wecom"));
    }

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
