using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 主要服务于IM的配置
/// </summary>
public sealed class InstanceChatService(
    IInstanceRuntimeConversationService runtimeConversationService,
    IKingCrabHttpClient kingCrabHttpClient,
    HireBotDbContext dbContext,
    IRequestContextService requestContextService) : IInstanceChatService
{
    private const string InAppChannel = "inapp";
    private const string FeishuChannelUpdatePath = "/admin/channels/feishu/update";
    private const string FeishuChannelOverrideDeletePath = "/admin/channels/feishu/override";
    private const string DingTalkChannelUpdatePath = "/admin/channels/dingtalk/update";
    private const string DingTalkChannelOverrideDeletePath = "/admin/channels/dingtalk/override";
    
    /// <summary>
    /// 获取实例的聊天消息列表。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天时间线</returns>
    public async Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await runtimeConversationService.GetMessagesAsync(instanceId, InAppChannel, cancellationToken: cancellationToken);
    }

    #region 站内

    /// <summary>
    /// 发送消息给实例。站内用
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="request">消息请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聊天结果</returns>
    public async Task<ApiResponse<InstanceChatResultDto>> SendMessageAsync(
        string instanceId,
        SendInstanceChatMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return ApiResponse<InstanceChatResultDto>.ErrorResponse(400, "content 不能为空");
        }

        return await runtimeConversationService.SendMessageAsync(
            instanceId,
            InAppChannel,
            request.Content,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 清空实例的聊天消息。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<ApiResponse<bool>> ClearMessagesAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await runtimeConversationService.ClearMessagesAsync(instanceId, InAppChannel, cancellationToken: cancellationToken);
    }

    #endregion


    #region 飞书


    /// <summary>
    /// 更新飞书频道配置（新的 KingCrab 网关入口）。
    /// </summary>
    public async Task<ApiResponse<ImConfigResultDto>> UpdateFeishuChannelConfigAsync(
        string instanceId,
        ImConfigRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(400, "配置不能为空");
        }

        var access = await ResolveChannelConfigTargetAsync(instanceId, "feishu", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await SendFeishuChannelConfigAsync(
            new FeishuChannelConfig
            {
                Enabled = true,
                AppId = request.AppId,
                AppIdRef = "env:FEISHU_APP_ID",
                AppSecret = request.AppSecret,
                AppSecretRef = "env:FEISHU_APP_SECRET",
                GroupPolicy = "open",
                AllowedFromUserIds = []
            },
            ownerSubject,
            cancellationToken);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            var message = remoteResult.Data?.Message ?? remoteResult.Message;
            return ApiResponse<ImConfigResultDto>.ErrorResponse(remoteResult.StatusCode, string.IsNullOrWhiteSpace(message) ? "飞书配置更新失败" : message);
        }

        if (!remoteResult.Data.Success)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(remoteResult.Data.Error) ? "飞书配置更新失败" : remoteResult.Data.Error);
        }

        return ApiResponse<ImConfigResultDto>.SuccessResponse(
            new ImConfigResultDto("feishu", "url_callback", "active", remoteResult.Data.Message ?? "飞书配置已更新", DateTimeOffset.UtcNow));
    }

  

    /// <summary>
    /// 更新飞书频道配置。
    /// 调用 KingCrab Gateway 的 /admin/channels/feishu/update 接口，
    /// 应用新的配置并重新连接飞书频道。
    /// </summary>
    /// <param name="config">飞书频道配置</param>
    /// <param name="ownerSubject">所有者标识</param>
    /// <param name="authToken">认证令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>远程调用结果，包含操作状态</returns>
    /// <summary>
    /// 鑾峰彇椋炰功棰戦亾褰撳墠鐢熸晥閰嶇疆銆?
    /// </summary>
    public async Task<ApiResponse<FeishuChannelEffectiveConfigDto>> GetFeishuChannelEffectiveConfigAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveChannelConfigTargetAsync(instanceId, "feishu", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<FeishuChannelEffectiveConfigDto>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await kingCrabHttpClient.SendForJsonAsync<FeishuChannelEffectiveConfigDto>(
            HttpMethod.Get,
            "/admin/channels/feishu",
            body: null,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            return ApiResponse<FeishuChannelEffectiveConfigDto>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(remoteResult.Message) ? "鑾峰彇椋炰功褰撳墠閰嶇疆澶辫触" : remoteResult.Message);
        }

        return ApiResponse<FeishuChannelEffectiveConfigDto>.SuccessResponse(remoteResult.Data);
    }

    

    /// <summary>
    /// 清除飞书频道的运行时覆盖配置，恢复 appsettings 生效。
    /// </summary>
    public async Task<ApiResponse<bool>> ClearFeishuChannelOverrideAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveChannelConfigTargetAsync(instanceId, "feishu", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<bool>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
            HttpMethod.Delete,
            FeishuChannelOverrideDeletePath,
            body: null,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            var message = remoteResult.Data?.Message ?? remoteResult.Message;
            return ApiResponse<bool>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(message) ? "飞书覆盖配置清除失败" : message);
        }

        if (!remoteResult.Data.Success)
        {
            var message = remoteResult.Data.Error ?? remoteResult.Data.Message;
            return ApiResponse<bool>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(message) ? "飞书覆盖配置清除失败" : message);
        }

        return ApiResponse<bool>.SuccessResponse(
            true,
            string.IsNullOrWhiteSpace(remoteResult.Data.Message)
                ? "飞书覆盖配置已清除，已恢复 appsettings 生效"
                : remoteResult.Data.Message);
    }


    private async Task<RemoteCallResult<KingCrabOperationStatusResult>> SendFeishuChannelConfigAsync(
        FeishuChannelConfig config,
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return RemoteCallResult<KingCrabOperationStatusResult>.Failure(400, "配置不能为空");
        }

        return await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
            HttpMethod.Post,
            FeishuChannelUpdatePath,
            config,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);
    }



    #endregion


    #region 钉钉


    /// <summary>
    /// 获取钉钉频道当前生效配置。
    /// </summary>
    public async Task<ApiResponse<DingTalkChannelConfig>> GetDingTalkChannelEffectiveConfigAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveChannelConfigTargetAsync(instanceId, "dingtalk", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<DingTalkChannelConfig>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await kingCrabHttpClient.SendForJsonAsync<DingTalkChannelConfig>(
            HttpMethod.Get,
            "/admin/channels/dingtalk",
            body: null,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            return ApiResponse<DingTalkChannelConfig>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(remoteResult.Message) ? "获取钉钉当前配置失败" : remoteResult.Message);
        }

        return ApiResponse<DingTalkChannelConfig>.SuccessResponse(remoteResult.Data);
    }

    /// <summary>
    /// 更新钉钉频道配置（新的 KingCrab 网关入口）。
    /// </summary>
    public async Task<ApiResponse<ImConfigResultDto>> UpdateDingTalkChannelConfigAsync(
        string instanceId,
        DingTalkChannelConfig request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(400, "配置不能为空");
        }

        var access = await ResolveChannelConfigTargetAsync(instanceId, "dingtalk", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await SendDingTalkChannelConfigAsync(request, ownerSubject, cancellationToken);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            var message = remoteResult.Data?.Message ?? remoteResult.Message;
            return ApiResponse<ImConfigResultDto>.ErrorResponse(remoteResult.StatusCode, string.IsNullOrWhiteSpace(message) ? "钉钉配置更新失败" : message);
        }

        if (!remoteResult.Data.Success)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(remoteResult.Data.Error) ? "钉钉配置更新失败" : remoteResult.Data.Error);
        }

        return ApiResponse<ImConfigResultDto>.SuccessResponse(
            new ImConfigResultDto("dingtalk", "url_callback", "active", remoteResult.Data.Message ?? "钉钉配置已更新", DateTimeOffset.UtcNow));
    }


    private async Task<RemoteCallResult<KingCrabOperationStatusResult>> SendDingTalkChannelConfigAsync(
    DingTalkChannelConfig config,
    string ownerSubject,
    CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return RemoteCallResult<KingCrabOperationStatusResult>.Failure(400, "配置不能为空");
        }

        return await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
            HttpMethod.Post,
            DingTalkChannelUpdatePath,
            config,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);
    }



    /// <summary>
    /// 清除钉钉频道的运行时覆盖配置，恢复 appsettings 生效。
    /// </summary>
    public async Task<ApiResponse<bool>> ClearDingTalkChannelOverrideAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveChannelConfigTargetAsync(instanceId, "dingtalk", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<bool>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
            HttpMethod.Delete,
            DingTalkChannelOverrideDeletePath,
            body: null,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            var message = remoteResult.Data?.Message ?? remoteResult.Message;
            return ApiResponse<bool>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(message) ? "钉钉覆盖配置清除失败" : message);
        }

        if (!remoteResult.Data.Success)
        {
            var message = remoteResult.Data.Error ?? remoteResult.Data.Message;
            return ApiResponse<bool>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(message) ? "钉钉覆盖配置清除失败" : message);
        }

        return ApiResponse<bool>.SuccessResponse(
            true,
            string.IsNullOrWhiteSpace(remoteResult.Data.Message)
                ? "钉钉覆盖配置已清除，已恢复 appsettings 生效"
                : remoteResult.Data.Message);
    }


    #endregion


    #region 企业微信

    private const string WeComChannelUpdatePath = "/admin/channels/wecom/update";
    private const string WeComChannelOverrideDeletePath = "/admin/channels/wecom/override";

    /// <summary>
    /// 获取企业微信频道当前生效配置。
    /// </summary>
    public async Task<ApiResponse<WeComChannelEffectiveConfigDto>> GetWeComChannelEffectiveConfigAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveChannelConfigTargetAsync(instanceId, "wecom", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<WeComChannelEffectiveConfigDto>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await kingCrabHttpClient.SendForJsonAsync<WeComChannelEffectiveConfigDto>(
            HttpMethod.Get,
            "/admin/channels/wecom",
            body: null,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            return ApiResponse<WeComChannelEffectiveConfigDto>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(remoteResult.Message) ? "获取企业微信当前配置失败" : remoteResult.Message);
        }

        return ApiResponse<WeComChannelEffectiveConfigDto>.SuccessResponse(remoteResult.Data);
    }

    /// <summary>
    /// 更新企业微信频道配置（KingCrab 网关入口）。
    /// </summary>
    public async Task<ApiResponse<ImConfigResultDto>> UpdateWeComChannelConfigAsync(
        string instanceId,
        ImConfigRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(400, "配置不能为空");
        }

        var access = await ResolveChannelConfigTargetAsync(instanceId, "wecom", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await SendWeComChannelConfigAsync(
            new WeComChannelConfig
            {
                Enabled = true,
                BotId = request.BotId,
                BotSecret = request.BotSecret
            },
            ownerSubject,
            cancellationToken);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            var message = remoteResult.Data?.Message ?? remoteResult.Message;
            return ApiResponse<ImConfigResultDto>.ErrorResponse(remoteResult.StatusCode, string.IsNullOrWhiteSpace(message) ? "企业微信配置更新失败" : message);
        }

        if (!remoteResult.Data.Success)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(remoteResult.Data.Error) ? "企业微信配置更新失败" : remoteResult.Data.Error);
        }

        return ApiResponse<ImConfigResultDto>.SuccessResponse(
            new ImConfigResultDto("wecom", "url_callback", "active", remoteResult.Data.Message ?? "企业微信配置已更新", DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// 清除企业微信频道的运行时覆盖配置，恢复 appsettings 生效。
    /// </summary>
    public async Task<ApiResponse<bool>> ClearWeComChannelOverrideAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveChannelConfigTargetAsync(instanceId, "wecom", cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<bool>.ErrorResponse(access.Code, access.Message);
        }

        var ownerSubject = requestContextService.ResolveOwnerSubject();
        var remoteResult = await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
            HttpMethod.Delete,
            WeComChannelOverrideDeletePath,
            body: null,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        if (!remoteResult.Success || remoteResult.Data is null)
        {
            var message = remoteResult.Data?.Message ?? remoteResult.Message;
            return ApiResponse<bool>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(message) ? "企业微信覆盖配置清除失败" : message);
        }

        if (!remoteResult.Data.Success)
        {
            var message = remoteResult.Data.Error ?? remoteResult.Data.Message;
            return ApiResponse<bool>.ErrorResponse(
                remoteResult.StatusCode,
                string.IsNullOrWhiteSpace(message) ? "企业微信覆盖配置清除失败" : message);
        }

        return ApiResponse<bool>.SuccessResponse(
            true,
            string.IsNullOrWhiteSpace(remoteResult.Data.Message)
                ? "企业微信覆盖配置已清除，已恢复 appsettings 生效"
                : remoteResult.Data.Message);
    }

    private async Task<RemoteCallResult<KingCrabOperationStatusResult>> SendWeComChannelConfigAsync(
        WeComChannelConfig config,
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return RemoteCallResult<KingCrabOperationStatusResult>.Failure(400, "配置不能为空");
        }

        return await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
            HttpMethod.Post,
            WeComChannelUpdatePath,
            config,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);
    }

    #endregion


    #region 公用
    /// <summary>
    /// 前置检查
    /// </summary>
    /// <param name="instanceId"></param>
    /// <param name="platform"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>

    private async Task<ConfigAccessResult> ResolveChannelConfigTargetAsync(
        string instanceId,
        string platform,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ConfigAccessResult.Fail(400, "instanceId 不能为空");
        }

        var instance = await dbContext.Instances.FirstOrDefaultAsync(
            item => item.InstanceId == instanceId.Trim(),
            cancellationToken);
        if (instance is null)
        {
            return ConfigAccessResult.Fail(404, "实例不存在");
        }

        if (!string.Equals(instance.Status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigAccessResult.Fail(409, "只有已上岗实例可以配置 IM");
        }

        if (!string.Equals(instance.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(instance.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigAccessResult.Fail(409, "部门员工不配置 IM，请先创建个人分身");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        if (!string.Equals(instance.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return ConfigAccessResult.Fail(403, "无权配置该实例 IM");
        }

        return ConfigAccessResult.Ok(instance, platform);
    }
    #endregion



    #region 配置实体

    



    #endregion

    /// <summary>
    /// 飞书配置目标校验结果。
    /// </summary>
    /// <summary>
    /// KingCrab 管理接口的通用操作结果。
    /// </summary>
    private sealed class KingCrabOperationStatusResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? Mode { get; set; }
    }

    private sealed record ConfigAccessResult(bool Success, int Code, string Message, InstanceEntity? Instance, string Platform)
    {
        public static ConfigAccessResult Ok(InstanceEntity instance, string platform)
            => new(true, 200, string.Empty, instance, platform);

        public static ConfigAccessResult Fail(int code, string message)
            => new(false, code, message, null, string.Empty);
    }
}
