using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Security;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class ImWebhookService(
    HireBotDbContext dbContext,
    ISecretProtector secretProtector,
    IInstanceRuntimeConversationService runtimeConversationService,
    IImWebhookReplayContext replayContext,
    IHttpClientFactory httpClientFactory,
    ILogger<ImWebhookService> logger) : IImWebhookService
{
    private const string FeishuClientName = "Feishu";
    private const string DingTalkClientName = "DingTalk";
    private const string WeComClientName = "WeCom";

    public async Task<ApiResponse<ImWebhookHandleResultDto>> VerifyAsync(
        string platform,
        string instanceId,
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        if (normalizedPlatform is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "platform is invalid");
        }

        var config = await ResolveActiveConfigAsync(normalizedPlatform, instanceId, cancellationToken);
        if (!config.Success)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(config.Code, config.Message);
        }

        if (normalizedPlatform == "wecom")
        {
            return VerifyWeComUrl(config.Data!, query);
        }

        return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
            new ImWebhookHandleResultDto("verified", query.TryGetValue("challenge", out var challenge) ? challenge : null),
            "webhook verified");
    }

    public async Task<ApiResponse<ImWebhookHandleResultDto>> HandleAsync(
        string platform,
        string instanceId,
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlatform = NormalizePlatform(platform);
        if (normalizedPlatform is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "platform is invalid");
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "instanceId 不能为空");
        }

        var configResponse = await ResolveActiveConfigAsync(normalizedPlatform, instanceId, cancellationToken);
        if (!configResponse.Success)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(configResponse.Code, configResponse.Message);
        }

        var config = configResponse.Data!;

        if (normalizedPlatform == "feishu")
        {
            if (!ValidateFeishuSignature(payload, headers, secretProtector.Unprotect(config.VerificationToken)))
            {
                logger.LogWarning("Feishu webhook signature validation failed. InstanceId={InstanceId}", instanceId);
                return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(401, "飞书签名验证失败");
            }
        }
        else if (normalizedPlatform == "wecom")
        {
            return await HandleWeComAsync(instanceId, payload, headers, config, cancellationToken);
        }
        else if (!ValidateGenericToken(config, headers))
        {
            logger.LogWarning("IM webhook token validation failed. Platform={Platform}, InstanceId={InstanceId}", normalizedPlatform, instanceId);
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(401, "IM 签名验证失败");
        }

        if (normalizedPlatform == "feishu")
        {
            return await HandleFeishuAsync(instanceId, payload, config, cancellationToken);
        }

        return await HandleGenericAsync(instanceId, normalizedPlatform, payload, config, cancellationToken);
    }

    public async Task<ApiResponse<string?>> ExtractFeishuUrlVerificationChallengeAsync(
        string instanceId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var inbound = TryParseFeishuMessage(payload);
        if (inbound is null)
        {
            return ApiResponse<string?>.SuccessResponse(null);
        }

        if (!string.IsNullOrWhiteSpace(inbound.VerificationEcho))
        {
            return ApiResponse<string?>.SuccessResponse(inbound.VerificationEcho);
        }

        if (string.IsNullOrWhiteSpace(inbound.Encrypt))
        {
            return ApiResponse<string?>.SuccessResponse(null);
        }

        var configResponse = await ResolveActiveConfigAsync("feishu", instanceId, cancellationToken);
        if (!configResponse.Success)
        {
            return ApiResponse<string?>.SuccessResponse(null);
        }

        var config = configResponse.Data!;
        var decryptedPayload = TryDecryptFeishuPayload(inbound.Encrypt, secretProtector.Unprotect(config.EncryptKey));
        if (decryptedPayload is null)
        {
            return ApiResponse<string?>.SuccessResponse(null);
        }

        var decryptedInbound = TryParseFeishuMessage(decryptedPayload);
        if (string.IsNullOrWhiteSpace(decryptedInbound?.VerificationEcho))
        {
            return ApiResponse<string?>.SuccessResponse(null);
        }

        var token = ExtractFeishuToken(decryptedPayload);
        var expectedToken = secretProtector.Unprotect(config.VerificationToken);
        if (!string.IsNullOrWhiteSpace(expectedToken) &&
            !string.IsNullOrWhiteSpace(token) &&
            !string.Equals(token, expectedToken, StringComparison.Ordinal))
        {
            logger.LogWarning("Feishu url verification token mismatch. InstanceId={InstanceId}", instanceId);
            return ApiResponse<string?>.ErrorResponse(401, "飞书 Verification Token 不匹配");
        }

        return ApiResponse<string?>.SuccessResponse(decryptedInbound.VerificationEcho);
    }

    private async Task<ApiResponse<Repository.Entities.ImConfigEntity>> ResolveActiveConfigAsync(
        string platform,
        string instanceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ApiResponse<Repository.Entities.ImConfigEntity>.ErrorResponse(400, "instanceId 不能为空");
        }

        var config = await dbContext.ImConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.InstanceId == instanceId.Trim() &&
                        item.Platform == platform,
                cancellationToken);
        if (config is null || !string.Equals(config.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<Repository.Entities.ImConfigEntity>.ErrorResponse(404, "该实例未配置可用 IM");
        }

        return ApiResponse<Repository.Entities.ImConfigEntity>.SuccessResponse(config);
    }

    private async Task<ApiResponse<ImWebhookHandleResultDto>> HandleFeishuAsync(
        string instanceId,
        string rawPayload,
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        var inbound = TryParseFeishuMessage(rawPayload);
        if (inbound is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "无法解析飞书消息");
        }

        if (!string.IsNullOrWhiteSpace(inbound.VerificationEcho))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("verified", inbound.VerificationEcho),
                "飞书验证通过");
        }

        var decryptedPayload = rawPayload;
        if (!string.IsNullOrWhiteSpace(inbound.Encrypt))
        {
            decryptedPayload = TryDecryptFeishuPayload(inbound.Encrypt!, secretProtector.Unprotect(config.EncryptKey));
            if (decryptedPayload is null)
            {
                return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "飞书消息解密失败");
            }

            inbound = TryParseFeishuMessage(decryptedPayload) ?? inbound;
        }

        if (!IsPrivateChat(inbound.ChatType))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", null),
                "feishu group chat ignored");
        }

        if (!IsTextMessage(inbound.Content))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", "unsupported non-text or file message"),
                "feishu non-text message ignored");
        }

        var content = inbound.Content!.Length > 4000 ? inbound.Content[..4000] : inbound.Content;
        if (inbound.Content.Length > 4000)
        {
            content += "\n\n[消息过长，已截断]";
        }

        var conversation = await runtimeConversationService.SendMessageAsync(
            instanceId,
            "feishu",
            content,
            config.OwnerUserId,
            inbound.MessageId,
            inbound.UserId,
            cancellationToken);
        if (!conversation.Success || conversation.Data?.AssistantMessage is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(
                conversation.Code,
                string.IsNullOrWhiteSpace(conversation.Message) ? "飞书消息处理失败" : conversation.Message);
        }

        var sendResult = await SendFeishuReplyAsync(
            inbound.UserId,
            conversation.Data.AssistantMessage.Content,
            config,
            cancellationToken);
        if (!sendResult.Success)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
            new ImWebhookHandleResultDto("replied", conversation.Data.AssistantMessage.Content));
    }

    private async Task<ApiResponse<ImWebhookHandleResultDto>> HandleGenericAsync(
        string instanceId,
        string platform,
        string rawPayload,
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        var inbound = TryParseGenericMessage(rawPayload);
        if (inbound is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "无法解析 IM 消息");
        }

        if (!IsPrivateChat(inbound.ChatType))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", null),
                "generic group chat ignored");
        }

        if (!IsTextMessage(inbound.Content))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", "unsupported non-text or file message"),
                "generic non-text message ignored");
        }

        var content = inbound.Content!.Length > 4000 ? inbound.Content[..4000] : inbound.Content;
        if (inbound.Content.Length > 4000)
        {
            content += "\n\n[消息过长，已截断]";
        }

        var conversation = await runtimeConversationService.SendMessageAsync(
            instanceId,
            platform,
            content,
            config.OwnerUserId,
            inbound.MessageId,
            inbound.UserId,
            cancellationToken);
        if (!conversation.Success || conversation.Data?.AssistantMessage is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(
                conversation.Code,
                string.IsNullOrWhiteSpace(conversation.Message) ? "IM 消息处理失败" : conversation.Message);
        }

        var sendResult = platform == "dingtalk"
            ? await SendDingTalkReplyAsync(inbound.UserId, conversation.Data.AssistantMessage.Content, config, cancellationToken)
            : await SendWeComReplyAsync(inbound.UserId, conversation.Data.AssistantMessage.Content, config, cancellationToken);
        if (!sendResult.Success)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
            new ImWebhookHandleResultDto("replied", conversation.Data.AssistantMessage.Content));
    }

    private ApiResponse<ImWebhookHandleResultDto> VerifyWeComUrl(
        Repository.Entities.ImConfigEntity config,
        IReadOnlyDictionary<string, string> query)
    {
        var msgSignature = QueryValue(query, "msg_signature");
        var timestamp = QueryValue(query, "timestamp");
        var nonce = QueryValue(query, "nonce");
        var echo = QueryValue(query, "echostr");
        var token = secretProtector.Unprotect(config.Token);
        var aesKey = secretProtector.Unprotect(config.AesKey);
        var corpId = secretProtector.Unprotect(config.CorpId);

        if (string.IsNullOrWhiteSpace(msgSignature) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(echo))
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "企业微信 URL 验证参数不完整");
        }

        var decrypted = DecryptWeComEncryptPayload(echo, msgSignature, timestamp, nonce, token, aesKey, corpId);
        if (decrypted is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(401, "企业微信 URL 验证失败");
        }

        return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
            new ImWebhookHandleResultDto("verified", decrypted),
            "企业微信 URL 验证通过");
    }

    private async Task<ApiResponse<ImWebhookHandleResultDto>> HandleWeComAsync(
        string instanceId,
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        var encrypted = TryExtractWeComEncrypt(rawPayload);
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "无法解析企业微信加密消息");
        }

        var signature = HeaderValue(headers, "msg_signature", "X-WeCom-Msg-Signature");
        var timestamp = HeaderValue(headers, "timestamp", "X-WeCom-Timestamp");
        var nonce = HeaderValue(headers, "nonce", "X-WeCom-Nonce");
        var token = secretProtector.Unprotect(config.Token);
        var aesKey = secretProtector.Unprotect(config.AesKey);
        var corpId = secretProtector.Unprotect(config.CorpId);
        var plainXml = DecryptWeComEncryptPayload(encrypted, signature, timestamp, nonce, token, aesKey, corpId);
        if (plainXml is null)
        {
            logger.LogWarning("WeCom webhook signature validation or decrypt failed. InstanceId={InstanceId}", instanceId);
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(401, "企业微信签名验证或消息解密失败");
        }

        var inbound = TryParseWeComMessage(plainXml);
        if (inbound is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "无法解析企业微信消息");
        }

        if (!string.Equals(inbound.MessageType, "text", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", "unsupported non-text or file message"),
                "wecom non-text message ignored");
        }

        if (!IsTextMessage(inbound.Content))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", "empty text message"),
                "wecom empty message ignored");
        }

        var content = inbound.Content!.Length > 4000 ? inbound.Content[..4000] : inbound.Content;
        if (inbound.Content.Length > 4000)
        {
            content += "\n\n[消息过长，已截断]";
        }

        var conversation = await runtimeConversationService.SendMessageAsync(
            instanceId,
            "wecom",
            content,
            config.OwnerUserId,
            inbound.MessageId,
            inbound.UserId,
            cancellationToken);
        if (!conversation.Success || conversation.Data?.AssistantMessage is null)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(
                conversation.Code,
                string.IsNullOrWhiteSpace(conversation.Message) ? "企业微信消息处理失败" : conversation.Message);
        }

        var sendResult = await SendWeComReplyAsync(
            inbound.UserId,
            conversation.Data.AssistantMessage.Content,
            config,
            cancellationToken);
        if (!sendResult.Success)
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
            new ImWebhookHandleResultDto("replied", conversation.Data.AssistantMessage.Content));
    }

    private bool ValidateGenericToken(
        Repository.Entities.ImConfigEntity config,
        IReadOnlyDictionary<string, string> headers)
    {
        var expected = secretProtector.Unprotect(config.Token) ?? secretProtector.Unprotect(config.VerificationToken);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        var candidates = new[]
        {
            "X-HireBot-Verification-Token",
            "X-Lark-Request-Token",
            "X-Dingtalk-Token",
            "X-WeCom-Token"
        };

        foreach (var key in candidates)
        {
            if (headers.TryGetValue(key, out var value) &&
                string.Equals(value, expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool ValidateFeishuSignature(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        string? verificationToken)
    {
        if (string.IsNullOrWhiteSpace(verificationToken))
        {
            return true;
        }

        if (!headers.TryGetValue("X-Lark-Signature", out var signature) ||
            !headers.TryGetValue("X-Lark-Request-Timestamp", out var timestamp) ||
            !headers.TryGetValue("X-Lark-Request-Nonce", out var nonce))
        {
            return false;
        }

        using var sha1 = SHA1.Create();
        var input = Encoding.UTF8.GetBytes($"{verificationToken.Trim()}{timestamp.Trim()}{nonce.Trim()}{rawPayload}");
        var expected = Convert.ToHexString(sha1.ComputeHash(input));
        return string.Equals(signature.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ApiResponse<bool>> SendFeishuReplyAsync(
        string receiveId,
        string content,
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        if (replayContext.SkipOutboundSend)
        {
            logger.LogInformation("Feishu outbound send skipped by replay context. InstanceId={InstanceId}, ReceiveId={ReceiveId}", config.InstanceId, receiveId);
            return ApiResponse<bool>.SuccessResponse(true, "feishu outbound send skipped");
        }

        var cleanedContent = RemoveThinkTags(content);
        var token = await GetFeishuTenantAccessTokenAsync(config, cancellationToken);
        if (!token.Success || string.IsNullOrWhiteSpace(token.Data))
        {
            return ApiResponse<bool>.ErrorResponse(token.Code, token.Message);
        }

        var client = httpClientFactory.CreateClient(FeishuClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/open-apis/im/v1/messages?receive_id_type=open_id");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Data);
        request.Content = JsonContent.Create(new
        {
            receive_id = receiveId,
            msg_type = "text",
            content = JsonSerializer.Serialize(new { text = cleanedContent })
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResponse<bool>.ErrorResponse((int)response.StatusCode, ExtractErrorMessage(responseContent) ?? "飞书消息发送失败");
        }

        return ApiResponse<bool>.SuccessResponse(true, "飞书消息已发送");
    }

    private async Task<ApiResponse<string>> GetFeishuTenantAccessTokenAsync(
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        var appId = secretProtector.Unprotect(config.AppId);
        var appSecret = secretProtector.Unprotect(config.AppSecret);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            return ApiResponse<string>.ErrorResponse(400, "feishu app_id/app_secret is not configured");
        }

        var client = httpClientFactory.CreateClient(FeishuClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/open-apis/auth/v3/tenant_access_token/internal");
        request.Content = JsonContent.Create(new
        {
            app_id = appId,
            app_secret = appSecret
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResponse<string>.ErrorResponse((int)response.StatusCode, ExtractErrorMessage(responseContent) ?? "飞书 tenant_access_token 获取失败");
        }

        try
        {
            using var document = JsonDocument.Parse(responseContent);
            if (document.RootElement.TryGetProperty("tenant_access_token", out var tokenElement) &&
                tokenElement.ValueKind == JsonValueKind.String)
            {
                var token = tokenElement.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return ApiResponse<string>.SuccessResponse(token, "ok");
                }
            }
        }
        catch (JsonException)
        {
            return ApiResponse<string>.ErrorResponse(502, "飞书 tenant_access_token 响应解析失败");
        }

        return ApiResponse<string>.ErrorResponse(502, "飞书 tenant_access_token 响应缺少 token");
    }

    private async Task<ApiResponse<bool>> SendDingTalkReplyAsync(
        string userId,
        string content,
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        if (replayContext.SkipOutboundSend)
        {
            logger.LogInformation("DingTalk outbound send skipped by replay context. InstanceId={InstanceId}, UserId={UserId}", config.InstanceId, userId);
            return ApiResponse<bool>.SuccessResponse(true, "dingtalk outbound send skipped");
        }

        var cleanedContent = RemoveThinkTags(content);
        var token = await GetDingTalkAccessTokenAsync(config, cancellationToken);
        if (!token.Success || string.IsNullOrWhiteSpace(token.Data))
        {
            return ApiResponse<bool>.ErrorResponse(token.Code, token.Message);
        }

        var agentId = secretProtector.Unprotect(config.AgentId);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return ApiResponse<bool>.ErrorResponse(400, "dingtalk agent_id is not configured");
        }

        var client = httpClientFactory.CreateClient(DingTalkClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/topapi/message/corpconversation/asyncsend_v2?access_token={Uri.EscapeDataString(token.Data)}");
        request.Content = JsonContent.Create(new
        {
            agent_id = NormalizeAgentId(agentId),
            userid_list = userId,
            msg = new
            {
                msgtype = "text",
                text = new { content = cleanedContent }
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResponse<bool>.ErrorResponse((int)response.StatusCode, ExtractErrorMessage(responseContent) ?? "钉钉消息发送失败");
        }

        return IsSuccessCode(responseContent)
            ? ApiResponse<bool>.SuccessResponse(true, "钉钉消息已发送")
            : ApiResponse<bool>.ErrorResponse(502, ExtractErrorMessage(responseContent) ?? "钉钉消息发送失败");
    }

    private async Task<ApiResponse<string>> GetDingTalkAccessTokenAsync(
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        var appId = secretProtector.Unprotect(config.AppId);
        var appSecret = secretProtector.Unprotect(config.AppSecret);
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            return ApiResponse<string>.ErrorResponse(400, "dingtalk app_id/app_secret is not configured");
        }

        var client = httpClientFactory.CreateClient(DingTalkClientName);
        using var response = await client.GetAsync(
            $"/gettoken?appkey={Uri.EscapeDataString(appId)}&appsecret={Uri.EscapeDataString(appSecret)}",
            cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResponse<string>.ErrorResponse((int)response.StatusCode, ExtractErrorMessage(responseContent) ?? "钉钉 access_token 获取失败");
        }

        return ExtractToken(responseContent, "access_token", "钉钉 access_token 响应缺少 token");
    }

    private async Task<ApiResponse<bool>> SendWeComReplyAsync(
        string userId,
        string content,
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        if (replayContext.SkipOutboundSend)
        {
            logger.LogInformation("WeCom outbound send skipped by replay context. InstanceId={InstanceId}, UserId={UserId}", config.InstanceId, userId);
            return ApiResponse<bool>.SuccessResponse(true, "wecom outbound send skipped");
        }

        var cleanedContent = RemoveThinkTags(content);
        var token = await GetWeComAccessTokenAsync(config, cancellationToken);
        if (!token.Success || string.IsNullOrWhiteSpace(token.Data))
        {
            return ApiResponse<bool>.ErrorResponse(token.Code, token.Message);
        }

        var agentId = secretProtector.Unprotect(config.AgentId);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return ApiResponse<bool>.ErrorResponse(400, "wecom agent_id is not configured");
        }

        var client = httpClientFactory.CreateClient(WeComClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/cgi-bin/message/send?access_token={Uri.EscapeDataString(token.Data)}");
        request.Content = JsonContent.Create(new
        {
            touser = userId,
            msgtype = "text",
            agentid = NormalizeAgentId(agentId),
            text = new { content = cleanedContent },
            safe = 0
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResponse<bool>.ErrorResponse((int)response.StatusCode, ExtractErrorMessage(responseContent) ?? "企业微信消息发送失败");
        }

        return IsSuccessCode(responseContent)
            ? ApiResponse<bool>.SuccessResponse(true, "企业微信消息已发送")
            : ApiResponse<bool>.ErrorResponse(502, ExtractErrorMessage(responseContent) ?? "企业微信消息发送失败");
    }

    private async Task<ApiResponse<string>> GetWeComAccessTokenAsync(
        Repository.Entities.ImConfigEntity config,
        CancellationToken cancellationToken)
    {
        var corpId = secretProtector.Unprotect(config.CorpId);
        var agentSecret = secretProtector.Unprotect(config.AgentSecret);
        if (string.IsNullOrWhiteSpace(corpId) || string.IsNullOrWhiteSpace(agentSecret))
        {
            return ApiResponse<string>.ErrorResponse(400, "wecom corp_id/agent_secret is not configured");
        }

        var client = httpClientFactory.CreateClient(WeComClientName);
        using var response = await client.GetAsync(
            $"/cgi-bin/gettoken?corpid={Uri.EscapeDataString(corpId)}&corpsecret={Uri.EscapeDataString(agentSecret)}",
            cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ApiResponse<string>.ErrorResponse((int)response.StatusCode, ExtractErrorMessage(responseContent) ?? "企业微信 access_token 获取失败");
        }

        return ExtractToken(responseContent, "access_token", "企业微信 access_token 响应缺少 token");
    }

    private static ApiResponse<string> ExtractToken(string responseContent, string propertyName, string missingMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(responseContent);
            if (document.RootElement.TryGetProperty(propertyName, out var tokenElement) &&
                tokenElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                return ApiResponse<string>.SuccessResponse(tokenElement.GetString(), "ok");
            }
        }
        catch (JsonException)
        {
            return ApiResponse<string>.ErrorResponse(502, "access_token 响应解析失败");
        }

        return ApiResponse<string>.ErrorResponse(502, missingMessage);
    }

    private static bool IsSuccessCode(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errcode", out var errcode) && errcode.TryGetInt32(out var errorCode))
            {
                return errorCode == 0;
            }

            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var responseCode))
            {
                return responseCode == 0;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return true;
    }

    private static object NormalizeAgentId(string agentId)
    {
        return long.TryParse(agentId.Trim(), out var numericAgentId)
            ? numericAgentId
            : agentId.Trim();
    }

    private static string? ExtractErrorMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString();
            }

            if (document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            if (document.RootElement.TryGetProperty("errmsg", out var errmsg) && errmsg.ValueKind == JsonValueKind.String)
            {
                return errmsg.GetString();
            }
        }
        catch (JsonException)
        {
            return content.Length > 200 ? content[..200] : content;
        }

        return null;
    }

    private static string? TryDecryptFeishuPayload(string encryptedContent, string? encryptKey)
    {
        if (string.IsNullOrWhiteSpace(encryptedContent) || string.IsNullOrWhiteSpace(encryptKey))
        {
            return null;
        }

        try
        {
            var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(encryptKey.Trim()));
            var cipherBytes = Convert.FromBase64String(encryptedContent);
            if (cipherBytes.Length <= 16)
            {
                return null;
            }

            using var aes = Aes.Create();
            aes.Key = keyBytes;
            aes.IV = cipherBytes[..16];
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 16, cipherBytes.Length - 16);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    private static string? DecryptWeComEncryptPayload(
        string encryptedContent,
        string? msgSignature,
        string? timestamp,
        string? nonce,
        string? token,
        string? encodingAesKey,
        string? receiveId)
    {
        if (string.IsNullOrWhiteSpace(encryptedContent) ||
            string.IsNullOrWhiteSpace(msgSignature) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(encodingAesKey))
        {
            return null;
        }

        if (!ValidateWeComSignature(token, timestamp, nonce, encryptedContent, msgSignature))
        {
            return null;
        }

        var key = DecodeWeComAesKey(encodingAesKey);
        if (key is null)
        {
            return null;
        }

        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = key[..16];
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            var cipherBytes = Convert.FromBase64String(encryptedContent);
            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            var unpadded = RemovePkcs7Padding(plainBytes);
            if (unpadded is null || unpadded.Length < 20)
            {
                return null;
            }

            var messageLength = ReadNetworkOrderInt(unpadded, 16);
            if (messageLength < 0 || 20 + messageLength > unpadded.Length)
            {
                return null;
            }

            var message = Encoding.UTF8.GetString(unpadded, 20, messageLength);
            var appId = Encoding.UTF8.GetString(unpadded, 20 + messageLength, unpadded.Length - 20 - messageLength);
            if (!string.IsNullOrWhiteSpace(receiveId) &&
                !string.Equals(appId, receiveId.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            return message;
        }
        catch
        {
            return null;
        }
    }

    private static bool ValidateWeComSignature(
        string token,
        string timestamp,
        string nonce,
        string encryptedContent,
        string expectedSignature)
    {
        var values = new[] { token.Trim(), timestamp.Trim(), nonce.Trim(), encryptedContent.Trim() }
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var joined = string.Concat(values);
        var actual = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
        return string.Equals(actual, expectedSignature.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? DecodeWeComAesKey(string encodingAesKey)
    {
        var normalized = encodingAesKey.Trim();
        if (normalized.Length != 43)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(normalized + "=");
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? RemovePkcs7Padding(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        var pad = bytes[^1];
        if (pad < 1 || pad > 32 || pad > bytes.Length)
        {
            return null;
        }

        for (var i = bytes.Length - pad; i < bytes.Length; i++)
        {
            if (bytes[i] != pad)
            {
                return null;
            }
        }

        return bytes[..^pad];
    }

    private static int ReadNetworkOrderInt(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) |
               (bytes[offset + 1] << 16) |
               (bytes[offset + 2] << 8) |
               bytes[offset + 3];
    }

    private static string? TryExtractWeComEncrypt(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(rawPayload, LoadOptions.PreserveWhitespace);
            return document.Root?.Element("Encrypt")?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static WeComInboundMessage? TryParseWeComMessage(string plainXml)
    {
        try
        {
            var document = XDocument.Parse(plainXml, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                return null;
            }

            var userId = ElementValue(root, "FromUserName");
            var content = ElementValue(root, "Content");
            var messageId = ElementValue(root, "MsgId");
            var messageType = ElementValue(root, "MsgType");
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return new WeComInboundMessage(
                string.IsNullOrWhiteSpace(messageId) ? BuildId("wecom") : messageId,
                userId,
                content,
                string.IsNullOrWhiteSpace(messageType) ? "text" : messageType);
        }
        catch
        {
            return null;
        }
    }

    private static string? ElementValue(XElement root, string name)
    {
        return root.Element(name)?.Value;
    }

    private static string? HeaderValue(IReadOnlyDictionary<string, string> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? QueryValue(IReadOnlyDictionary<string, string> values, string name)
    {
        return values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static FeishuInboundMessage? TryParseFeishuMessage(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            var challenge = FirstString(root, "challenge");
            var encrypt = FirstString(root, "encrypt");
            var messageId = FirstString(root, "message_id", "msg_id", "msgId");
            var userId = FirstString(root, "user_id", "open_id", "sender_id", "from_user_id");
            var chatType = FirstString(root, "chat_type", "conversation_type");
            var content = ExtractTextContent(root);

            if (root.TryGetProperty("event", out var eventElement))
            {
                challenge ??= FirstString(eventElement, "challenge");
                encrypt ??= FirstString(eventElement, "encrypt");
                messageId ??= FirstString(eventElement, "message_id", "msg_id", "msgId");
                userId ??= ExtractSenderId(eventElement);
                chatType ??= FirstString(eventElement, "chat_type", "conversation_type");
                content ??= ExtractTextContent(eventElement);

                if (eventElement.TryGetProperty("message", out var messageElement))
                {
                    messageId ??= FirstString(messageElement, "message_id", "msg_id", "msgId");
                    chatType ??= FirstString(messageElement, "chat_type", "conversation_type");
                    content ??= ExtractTextContent(messageElement);
                }

                if (eventElement.TryGetProperty("sender", out var senderElement))
                {
                    userId ??= ExtractSenderId(senderElement);
                }
            }

            return new FeishuInboundMessage(
                string.IsNullOrWhiteSpace(messageId) ? BuildId("feishu") : messageId,
                string.IsNullOrWhiteSpace(userId) ? "unknown" : userId,
                content,
                chatType,
                challenge,
                encrypt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractFeishuToken(string rawPayload)
    {
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            var token = FirstString(root, "token");
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            return root.TryGetProperty("event", out var eventElement)
                ? FirstString(eventElement, "token")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FeishuInboundMessage? TryParseGenericMessage(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            var content = FirstString(root, "content", "text");
            var messageId = FirstString(root, "message_id", "msg_id", "msgId");
            var userId = FirstString(root, "user_id", "open_id", "sender_id", "from_user_id", "senderStaffId", "fromUserName", "FromUserName");
            var chatType = FirstString(root, "chat_type", "conversation_type");

            if (root.TryGetProperty("event", out var eventElement))
            {
                content ??= FirstString(eventElement, "content", "text");
                messageId ??= FirstString(eventElement, "message_id", "msg_id", "msgId");
                userId ??= FirstString(eventElement, "user_id", "open_id", "sender_id", "from_user_id", "senderStaffId", "fromUserName", "FromUserName");
                chatType ??= FirstString(eventElement, "chat_type", "conversation_type", "conversationType");

                if (eventElement.TryGetProperty("message", out var messageElement))
                {
                    content ??= ExtractTextContent(messageElement);
                    messageId ??= FirstString(messageElement, "message_id", "msg_id", "msgId");
                    chatType ??= FirstString(messageElement, "chat_type", "conversation_type", "conversationType");
                }

                if (eventElement.TryGetProperty("text", out var textElement))
                {
                    content ??= ExtractTextContent(textElement);
                }

                if (eventElement.TryGetProperty("sender", out var senderElement))
                {
                    userId ??= ExtractSenderId(senderElement);
                }
            }

            if (root.TryGetProperty("text", out var rootTextElement))
            {
                content ??= ExtractTextContent(rootTextElement);
            }

            content ??= ExtractTextContent(root);
            return new FeishuInboundMessage(
                string.IsNullOrWhiteSpace(messageId) ? BuildId("immsg") : messageId,
                string.IsNullOrWhiteSpace(userId) ? "unknown" : userId,
                content,
                chatType,
                null,
                null);
        }
        catch (JsonException)
        {
            return new FeishuInboundMessage(BuildId("immsg"), "unknown", rawPayload.Trim(), null, null, null);
        }
    }

    private static string? ExtractTextContent(JsonElement element)
    {
        var raw = FirstString(element, "content", "text");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var nested = JsonDocument.Parse(trimmed);
            return FirstString(nested.RootElement, "text", "content");
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static string? ExtractSenderId(JsonElement senderElement)
    {
        if (senderElement.TryGetProperty("sender", out var nestedSender))
        {
            var nested = ExtractSenderId(nestedSender);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        var direct = FirstString(senderElement, "user_id", "open_id", "sender_id");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (senderElement.TryGetProperty("sender_id", out var senderIdElement))
        {
            return FirstString(senderIdElement, "user_id", "open_id", "union_id");
        }

        return null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static bool IsPrivateChat(string? chatType)
    {
        if (string.IsNullOrWhiteSpace(chatType))
        {
            return true;
        }

        var normalized = chatType.Trim().ToLowerInvariant();
        return normalized is "p2p" or "single" or "private" or "direct" or "1";
    }

    private static bool IsTextMessage(string? content)
    {
        return !string.IsNullOrWhiteSpace(content);
    }

    private static string? NormalizePlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        var normalized = platform.Trim().ToLowerInvariant();
        return normalized is "feishu" or "dingtalk" or "wecom" ? normalized : null;
    }

    private static string BuildId(string prefix)
    {
        return $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];
    }

    private static string RemoveThinkTags(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        return System.Text.RegularExpressions.Regex.Replace(content, @"\<think\>.*?\</think\>", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
    }

    private sealed record FeishuInboundMessage(
        string MessageId,
        string UserId,
        string? Content,
        string? ChatType,
        string? VerificationEcho,
        string? Encrypt);

    private sealed record WeComInboundMessage(
        string MessageId,
        string UserId,
        string? Content,
        string MessageType);
}
