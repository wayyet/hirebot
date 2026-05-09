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
    IInstanceImConfigService instanceImConfigService) : ControllerBase
{
    /// <summary>
    /// 获取实例的应用内聊天消息列表
    /// </summary>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天消息列表</returns>
    [HttpGet("{instanceId}/inapp-chat/messages")]
    public async Task<IActionResult> GetInAppChatMessages(string instanceId, CancellationToken cancellationToken = default)
    {
        var response = await instanceChatService.GetMessagesAsync(instanceId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 发送应用内聊天消息
    /// </summary>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="request">消息发送请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>消息发送结果</returns>
    [HttpPost("{instanceId}/inapp-chat/messages")]
    public async Task<IActionResult> SendInAppChatMessage(
        string instanceId,
        [FromBody] SendInstanceChatMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // 参数校验
        var invalidResponse = BuildModelValidationError<InstanceChatResultDto>();
        if (invalidResponse is not null)
        {
            return invalidResponse;
        }

        // 发送消息
        var response = await instanceChatService.SendMessageAsync(instanceId, request, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 清空实例的应用内聊天消息
    /// </summary>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{instanceId}/inapp-chat/messages")]
    public async Task<IActionResult> ClearInAppChatMessages(string instanceId, CancellationToken cancellationToken = default)
    {
        var response = await instanceChatService.ClearMessagesAsync(instanceId, cancellationToken);
        return StatusCode(response.Code, response);
    }

    /// <summary>
    /// 获取实例的 IM Webhook 地址
    /// </summary>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="platform">IM 平台名称（如: wechat, dingtalk, feishu）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Webhook 地址</returns>
    [HttpGet("{instanceId}/im-webhook-url")]
    public async Task<IActionResult> GetImWebhookUrl(
        string instanceId,
        [FromQuery] string platform,
        CancellationToken cancellationToken = default)
    {
        var response = await instanceImConfigService.GetWebhookUrlAsync(instanceId, platform, cancellationToken);
        return StatusCode(response.Code, response);
    }

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

        // 更新其他平台的配置，继续走原来的实例 IM 配置服务
        var response = await instanceImConfigService.UpsertConfigAsync(instanceId, platform, request, cancellationToken);
        return StatusCode(response.Code, response);
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
        var response = ApiResponse<FeishuChannelEffectiveConfigDto>.ErrorResponse(
            404,
            $"Unknown or unsupported platform '{platform}'.");
        return StatusCode(response.Code, response);
    }

    [HttpGet("{instanceId}/im-config")]
    public async Task<IActionResult> GetImConfigs(string instanceId, CancellationToken cancellationToken = default)
    {
        var response = await instanceImConfigService.GetConfigsAsync(instanceId, cancellationToken);
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
            var feishuResponse = await instanceChatService.ClearDingTalkChannelOverrideAsync(instanceId, cancellationToken);
            return StatusCode(feishuResponse.Code, feishuResponse);
        }

        var response = await instanceImConfigService.DeleteConfigAsync(instanceId, platform, cancellationToken);
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
