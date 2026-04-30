using HireBot.Abstraction.Services.EmployeeRuntime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

/// <summary>
/// IM 平台 Webhook 控制器
/// 处理来自各种即时通讯平台的 Webhook 事件通知
/// </summary>
[Route("api/v1/im")]
[ApiController]
[AllowAnonymous]
public sealed class ImWebhooksController(IImWebhookService imWebhookService) : ControllerBase
{
    /// <summary>
    /// 处理 IM 平台的 Webhook 请求
    /// </summary>
    /// <param name="platform">IM 平台名称（如: wechat, dingtalk, feishu）</param>
    /// <param name="instanceId">员工实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Webhook 处理响应</returns>
    [HttpPost("{platform}/webhook/{instanceId}")]
    public async Task<IActionResult> Handle(
        string platform,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        // 读取请求体内容
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        
        // 将请求头转换为字典（忽略大小写）
        var headers = Request.Headers.ToDictionary(
            item => item.Key,
            item => item.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        // 委托给 Webhook 服务处理
        var response = await imWebhookService.HandleAsync(platform, instanceId, payload, headers, cancellationToken);
        return StatusCode(response.Code, response);
    }
}

