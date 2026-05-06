using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 实例 IM 配置服务，管理实例与外部 IM 平台的连接配置。
/// </summary>
public sealed class InstanceImConfigService(
    HireBotDbContext dbContext,
    IRequestContextService requestContextService,
    ISecretProtector secretProtector,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor) : IInstanceImConfigService
{
    private static readonly string[] Platforms = ["feishu", "dingtalk", "wecom"];

    /// <summary>
    /// 获取实例的 Webhook URL。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="platform">平台类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Webhook URL</returns>
    public async Task<ApiResponse<ImWebhookUrlDto>> GetWebhookUrlAsync(
        string instanceId,
        string platform,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveConfigAccessAsync(instanceId, platform, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<ImWebhookUrlDto>.ErrorResponse(access.Code, access.Message);
        }

        var webhookPath = BuildWebhookPath(access.Platform, access.Instance!.InstanceId);
        return ApiResponse<ImWebhookUrlDto>.SuccessResponse(
            new ImWebhookUrlDto(access.Platform, $"{ResolvePublicBaseUrl()}{webhookPath}"));
    }

    /// <summary>
    /// 创建或更新实例的 IM 配置。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="platform">平台类型</param>
    /// <param name="request">配置请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>配置结果</returns>
    public async Task<ApiResponse<ImConfigResultDto>> UpsertConfigAsync(
        string instanceId,
        string platform,
        ImConfigRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(400, "请求体不能为空");
        }

        var access = await ResolveConfigAccessAsync(instanceId, platform, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(access.Code, access.Message);
        }

        var mode = NormalizeConnectionMode(request.ConnectionMode);
        if (mode is null)
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(400, "connection_mode 只能是 websocket 或 url_callback");
        }

        var validationError = ValidateCredentialShape(access.Platform, mode, request);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return ApiResponse<ImConfigResultDto>.ErrorResponse(400, validationError);
        }

        var now = DateTimeOffset.UtcNow;
        var config = await dbContext.ImConfigs.FirstOrDefaultAsync(
            item => item.InstanceId == access.Instance!.InstanceId && item.Platform == access.Platform,
            cancellationToken);

        if (config is null)
        {
            config = new ImConfigEntity
            {
                ConfigId = BuildId("imcfg"),
                InstanceId = access.Instance!.InstanceId,
                TenantId = access.Instance.TenantId,
                OwnerUserId = access.Instance.OwnerUserId,
                Platform = access.Platform,
                ConnectionMode = mode,
                WebhookPath = BuildWebhookPath(access.Platform, access.Instance.InstanceId),
                Status = "active",
                ConfiguredAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ImConfigs.Add(config);
        }
        else
        {
            config.ConnectionMode = mode;
            config.WebhookPath = BuildWebhookPath(access.Platform, access.Instance!.InstanceId);
            config.Status = "active";
            config.LastError = null;
            config.ConfiguredAt = now;
            config.UpdatedAt = now;
        }

        config.AppId = secretProtector.Protect(request.AppId);
        config.AppSecret = secretProtector.Protect(request.AppSecret);
        config.EncryptKey = secretProtector.Protect(request.EncryptKey);
        config.Token = secretProtector.Protect(request.Token);
        config.AesKey = secretProtector.Protect(request.AesKey);
        config.VerificationToken = secretProtector.Protect(request.VerificationToken);
        config.CorpId = secretProtector.Protect(request.CorpId);
        config.AgentId = secretProtector.Protect(request.AgentId);
        config.AgentSecret = secretProtector.Protect(request.AgentSecret);

        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<ImConfigResultDto>.SuccessResponse(
            new ImConfigResultDto(access.Platform, mode, config.Status, "连接配置已保存", config.ConfiguredAt));
    }

    /// <summary>
    /// 获取实例的所有 IM 配置状态。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>配置状态列表</returns>
    public async Task<ApiResponse<ImConfigStatusDto>> GetConfigsAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveInstanceAsync(instanceId, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<ImConfigStatusDto>.ErrorResponse(access.Code, access.Message);
        }

        var configs = await dbContext.ImConfigs
            .AsNoTracking()
            .Where(item => item.InstanceId == access.Instance!.InstanceId)
            .ToDictionaryAsync(item => item.Platform, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var items = Platforms.Select(platform =>
        {
            if (!configs.TryGetValue(platform, out var config))
            {
                return new ImConfigItemDto(platform, "unconfigured", null, BuildWebhookPath(platform, access.Instance!.InstanceId), null, null);
            }

            return new ImConfigItemDto(
                platform,
                config.Status,
                config.ConnectionMode,
                config.WebhookPath,
                config.ConfiguredAt,
                config.LastError,
                secretProtector.Unprotect(config.AppId),
                secretProtector.Unprotect(config.AppSecret),
                secretProtector.Unprotect(config.EncryptKey),
                secretProtector.Unprotect(config.Token),
                secretProtector.Unprotect(config.AesKey),
                secretProtector.Unprotect(config.VerificationToken),
                secretProtector.Unprotect(config.CorpId),
                secretProtector.Unprotect(config.AgentId),
                secretProtector.Unprotect(config.AgentSecret));
        }).ToArray();

        return ApiResponse<ImConfigStatusDto>.SuccessResponse(new ImConfigStatusDto(items));
    }

    /// <summary>
    /// 删除实例的 IM 配置。
    /// </summary>
    /// <param name="instanceId">实例ID</param>
    /// <param name="platform">平台类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除结果</returns>
    public async Task<ApiResponse<bool>> DeleteConfigAsync(
        string instanceId,
        string platform,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveConfigAccessAsync(instanceId, platform, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse<bool>.ErrorResponse(access.Code, access.Message);
        }

        var config = await dbContext.ImConfigs.FirstOrDefaultAsync(
            item => item.InstanceId == access.Instance!.InstanceId && item.Platform == access.Platform,
            cancellationToken);

        if (config is not null)
        {
            dbContext.ImConfigs.Remove(config);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ApiResponse<bool>.SuccessResponse(true, "IM 配置已撤销");
    }

    /// <summary>
    /// 解析配置访问权限。
    /// </summary>
    private async Task<ConfigAccessResult> ResolveConfigAccessAsync(
        string instanceId,
        string platform,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ConfigAccessResult.Fail(400, "instanceId 不能为空");
        }

        var normalizedPlatform = NormalizePlatform(platform);
        if (normalizedPlatform is null)
        {
            return ConfigAccessResult.Fail(400, "platform 不合法");
        }

        var instanceAccess = await ResolveInstanceAsync(instanceId, cancellationToken);
        if (!instanceAccess.Success)
        {
            return ConfigAccessResult.Fail(instanceAccess.Code, instanceAccess.Message);
        }

        return ConfigAccessResult.Ok(instanceAccess.Instance!, normalizedPlatform);
    }

    /// <summary>
    /// 解析实例访问权限。
    /// </summary>
    private async Task<ConfigAccessResult> ResolveInstanceAsync(string instanceId, CancellationToken cancellationToken)
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

        return ConfigAccessResult.Ok(instance, string.Empty);
    }

    /// <summary>
    /// 规范化平台名称。
    /// </summary>
    private static string? NormalizePlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        var normalized = platform.Trim().ToLowerInvariant();
        return Platforms.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : null;
    }

    /// <summary>
    /// 规范化连接模式。
    /// </summary>
    private static string? NormalizeConnectionMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        var normalized = mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "websocket" => "websocket",
            "url_callback" or "webhook" or "callback" => "url_callback",
            _ => null
        };
    }

    /// <summary>
    /// 验证凭证格式。
    /// </summary>
    private static string? ValidateCredentialShape(string platform, string mode, ImConfigRequestDto request)
    {
        static bool Missing(string? value) => string.IsNullOrWhiteSpace(value);

        if (platform == "wecom")
        {
            if (mode != "url_callback")
            {
                return "企业微信仅支持 url_callback 模式";
            }

            if (Missing(request.Token) || Missing(request.AesKey) || Missing(request.CorpId) ||
                Missing(request.AgentId) || Missing(request.AgentSecret))
            {
                return "企业微信 URL 回调模式需提供 token、aes_key、corp_id、agent_id、agent_secret";
            }

            return null;
        }

        if (platform == "feishu" && mode == "url_callback" &&
            (Missing(request.AppId) || Missing(request.AppSecret) || Missing(request.EncryptKey) ||
             Missing(request.VerificationToken)))
        {
            return "飞书 URL 回调模式需提供 app_id、app_secret、encrypt_key、verification_token";
        }

        if (platform == "dingtalk" && mode == "url_callback" &&
            (Missing(request.AppId) || Missing(request.AppSecret) || Missing(request.EncryptKey) || Missing(request.AgentId)))
        {
            return "钉钉 URL 回调模式需提供 app_id、app_secret、encrypt_key、agent_id";
        }

        if (platform == "dingtalk" && mode == "websocket" &&
            (Missing(request.AppId) || Missing(request.AppSecret) || Missing(request.AgentId)))
        {
            return "钉钉 WebSocket 模式需提供 app_id、app_secret、agent_id";
        }

        if (mode == "websocket" && (Missing(request.AppId) || Missing(request.AppSecret)))
        {
            return "WebSocket 模式需提供 app_id 和 app_secret";
        }

        return null;
    }

    /// <summary>
    /// 解析公共基础 URL。
    /// </summary>
    private string ResolvePublicBaseUrl()
    {
        var configured = configuration["HireBot:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is not null)
        {
            return $"{request.Scheme}://{request.Host}".TrimEnd('/');
        }

        return string.Empty;
    }

    /// <summary>
    /// 构建 Webhook 路径。
    /// </summary>
    private static string BuildWebhookPath(string platform, string instanceId)
    {
        return $"/api/v1/im/{platform}/webhook/{Uri.EscapeDataString(instanceId)}";
    }

    /// <summary>
    /// 构建唯一 ID。
    /// </summary>
    private static string BuildId(string prefix)
    {
        return $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];
    }

    /// <summary>
    /// 配置访问结果。
    /// </summary>
    private sealed record ConfigAccessResult(bool Success, int Code, string Message, InstanceEntity? Instance, string Platform)
    {
        public static ConfigAccessResult Ok(InstanceEntity instance, string platform)
        {
            return new ConfigAccessResult(true, 200, string.Empty, instance, platform);
        }

        public static ConfigAccessResult Fail(int code, string message)
        {
            return new ConfigAccessResult(false, code, message, null, string.Empty);
        }
    }
}