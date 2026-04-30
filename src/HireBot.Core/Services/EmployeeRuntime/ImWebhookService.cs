using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    IHttpClientFactory httpClientFactory,
    ILogger<ImWebhookService> logger) : IImWebhookService
{
    private const string FeishuClientName = "Feishu";

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
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "platform 不合法");
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(400, "instanceId 不能为空");
        }

        var config = await dbContext.ImConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.InstanceId == instanceId.Trim() &&
                        item.Platform == normalizedPlatform,
                cancellationToken);
        if (config is null || !string.Equals(config.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(404, "该实例未配置可用 IM");
        }

        if (normalizedPlatform == "feishu")
        {
            if (!ValidateFeishuSignature(payload, headers, secretProtector.Unprotect(config.VerificationToken)))
            {
                logger.LogWarning("Feishu webhook signature validation failed. InstanceId={InstanceId}", instanceId);
                return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(401, "飞书签名校验失败");
            }
        }
        else if (!ValidateGenericToken(config, headers))
        {
            logger.LogWarning("IM webhook token validation failed. Platform={Platform}, InstanceId={InstanceId}", normalizedPlatform, instanceId);
            return ApiResponse<ImWebhookHandleResultDto>.ErrorResponse(401, "IM 签名校验失败");
        }

        if (normalizedPlatform == "feishu")
        {
            return await HandleFeishuAsync(instanceId, payload, config, cancellationToken);
        }

        return await HandleGenericAsync(instanceId, normalizedPlatform, payload, config, cancellationToken);
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
                "飞书群聊消息已忽略");
        }

        if (!IsTextMessage(inbound.Content))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", "暂不支持文件或非文本消息，请发送文本"),
                "飞书非文本消息已忽略");
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
                "群聊消息已忽略");
        }

        if (!IsTextMessage(inbound.Content))
        {
            return ApiResponse<ImWebhookHandleResultDto>.SuccessResponse(
                new ImWebhookHandleResultDto("ignored", "暂不支持文件或非文本消息，请发送文本"),
                "非文本消息已忽略");
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
            content = JsonSerializer.Serialize(new { text = content })
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
            return ApiResponse<string>.ErrorResponse(400, "飞书 app_id/app_secret 未配置");
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
            var paddedKey = encryptKey.Trim();
            while (paddedKey.Length % 4 != 0)
            {
                paddedKey += "=";
            }

            var keyBytes = Convert.FromBase64String(paddedKey);
            if (keyBytes.Length != 32)
            {
                return null;
            }

            using var aes = Aes.Create();
            aes.Key = keyBytes;
            aes.IV = keyBytes[..16];
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var cipherBytes = Convert.FromBase64String(encryptedContent);
            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
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
            var userId = FirstString(root, "user_id", "open_id", "sender_id", "from_user_id");
            var chatType = FirstString(root, "chat_type", "conversation_type");

            if (root.TryGetProperty("event", out var eventElement))
            {
                content ??= FirstString(eventElement, "content", "text");
                messageId ??= FirstString(eventElement, "message_id", "msg_id", "msgId");
                userId ??= FirstString(eventElement, "user_id", "open_id", "sender_id", "from_user_id");
                chatType ??= FirstString(eventElement, "chat_type", "conversation_type");

                if (eventElement.TryGetProperty("message", out var messageElement))
                {
                    content ??= ExtractTextContent(messageElement);
                    messageId ??= FirstString(messageElement, "message_id", "msg_id", "msgId");
                    chatType ??= FirstString(messageElement, "chat_type", "conversation_type");
                }

                if (eventElement.TryGetProperty("sender", out var senderElement))
                {
                    userId ??= ExtractSenderId(senderElement);
                }
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
        return normalized is "p2p" or "single" or "private" or "direct";
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

    private sealed record FeishuInboundMessage(
        string MessageId,
        string UserId,
        string? Content,
        string? ChatType,
        string? VerificationEcho,
        string? Encrypt);
}
