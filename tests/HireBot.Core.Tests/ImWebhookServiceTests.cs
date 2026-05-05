using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class ImWebhookServiceTests
{
    [Fact]
    public async Task HandleAsync_WithValidFeishuPayload_RoutesToRuntimeConversation_AndSendsReply()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext);

        var handler = new RecordingHttpMessageHandler(
            request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/auth/v3/tenant_access_token/internal", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"tenant_access_token":"tenant-token","expire":7200}""", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri!.AbsolutePath.Contains("/open-apis/im/v1/messages", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"code":0,"msg":"ok"}""", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found")
                };
            });

        var runtime = new FakeRuntimeConversationService();
        var service = CreateService(dbContext, runtime, handler);
        var payload = """
        {
          "event": {
            "sender": { "open_id": "ou_1" },
            "message": {
              "message_id": "im_1",
              "chat_type": "p2p",
              "content": "{\"text\":\"帮我总结\"}"
            }
          }
        }
        """;

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            payload,
            BuildFeishuHeaders(payload, "verify"));

        Assert.True(response.Success, response.Message);
        Assert.Equal("replied", response.Data!.Status);
        Assert.Equal("runtime reply", response.Data.Reply);
        Assert.Single(runtime.Calls);
        Assert.Equal("feishu", runtime.Calls[0].Channel);
        Assert.Equal("pc_1", runtime.Calls[0].InstanceId);
        Assert.Equal("帮我总结", runtime.Calls[0].Content);
        Assert.Equal("owner-1", runtime.Calls[0].OwnerUserId);
        Assert.Equal("im_1", runtime.Calls[0].ExternalMessageId);
        Assert.Equal("ou_1", runtime.Calls[0].ExternalUserId);

        var sendRequest = handler.Requests.Single(request => request.RequestUri!.AbsolutePath.Contains("/open-apis/im/v1/messages", StringComparison.OrdinalIgnoreCase));
        var sendBody = await sendRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"receive_id\":\"ou_1\"", sendBody);
        Assert.Contains("\"msg_type\":\"text\"", sendBody);
        Assert.Contains("runtime reply", sendBody);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidToken_ReturnsUnauthorized()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext);
        var runtime = new FakeRuntimeConversationService();
        var service = CreateService(dbContext, runtime);

        var payload = """{"event":{"sender":{"open_id":"ou_1"},"message":{"message_id":"im_1","chat_type":"p2p","content":"{\"text\":\"hello\"}"}}}""";

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            payload,
            BuildFeishuHeaders(payload, "wrong"));

        Assert.False(response.Success);
        Assert.Equal(401, response.Code);
        Assert.Empty(runtime.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithGroupChatPayload_IgnoresMessage()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext);
        var runtime = new FakeRuntimeConversationService();
        var service = CreateService(dbContext, runtime);

        var payload = """{"event":{"sender":{"open_id":"ou_1"},"message":{"message_id":"im_1","chat_type":"group","content":"{\"text\":\"hello\"}"}}}""";

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            payload,
            BuildFeishuHeaders(payload, "verify"));

        Assert.True(response.Success, response.Message);
        Assert.Equal("ignored", response.Data!.Status);
        Assert.Empty(runtime.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithVerificationChallenge_ReturnsEcho()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext);
        var runtime = new FakeRuntimeConversationService();
        var service = CreateService(dbContext, runtime);

        var payload = """{"challenge":"echo-123"}""";

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            payload,
            BuildFeishuHeaders(payload, "verify"));

        Assert.True(response.Success, response.Message);
        Assert.Equal("verified", response.Data!.Status);
        Assert.Equal("echo-123", response.Data.Reply);
        Assert.Empty(runtime.Calls);
    }

    [Fact]
    public async Task HandleAsync_WhenReplayContextSkipsOutboundSend_DoesNotCallFeishuSend()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext);

        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tenant_access_token":"tenant-token","expire":7200}""", Encoding.UTF8, "application/json")
        });
        var runtime = new FakeRuntimeConversationService();
        var replayContext = new FakeReplayContext
        {
            SkipOutboundSend = true,
            UseMockKingCrew = true
        };
        var service = CreateService(dbContext, runtime, handler, replayContext);
        var payload = """
        {
          "event": {
            "sender": { "open_id": "ou_1" },
            "message": {
              "message_id": "im_1",
              "chat_type": "p2p",
              "content": "{\"text\":\"hello\"}"
            }
          }
        }
        """;

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            payload,
            BuildFeishuHeaders(payload, "verify"));

        Assert.True(response.Success, response.Message);
        Assert.Equal("replied", response.Data!.Status);
        Assert.Equal("runtime reply", response.Data.Reply);
        Assert.Single(runtime.Calls);
        Assert.Empty(handler.Requests.Where(request => request.RequestUri!.AbsolutePath.Contains("/open-apis/im/v1/messages", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task HandleAsync_WithValidDingTalkPayload_RoutesToRuntimeConversation_AndSendsReply()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext, "dingtalk");

        var handler = new RecordingHttpMessageHandler(
            request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/gettoken", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"errcode":0,"errmsg":"ok","access_token":"dingtalk-token"}""", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri!.AbsolutePath.Contains("/topapi/message/corpconversation/asyncsend_v2", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"errcode":0,"errmsg":"ok","task_id":123}""", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found")
                };
            });

        var runtime = new FakeRuntimeConversationService();
        var service = CreateService(dbContext, runtime, handler);
        var payload = """
        {
          "event": {
            "msgId": "dt_1",
            "senderStaffId": "manager001",
            "conversationType": "1",
            "text": { "content": "查询今天的任务" }
          }
        }
        """;

        var response = await service.HandleAsync(
            "dingtalk",
            "pc_1",
            payload,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Dingtalk-Token"] = "verify"
            });

        Assert.True(response.Success, response.Message);
        Assert.Equal("replied", response.Data!.Status);
        Assert.Single(runtime.Calls);
        Assert.Equal("dingtalk", runtime.Calls[0].Channel);
        Assert.Equal("查询今天的任务", runtime.Calls[0].Content);
        Assert.Equal("dt_1", runtime.Calls[0].ExternalMessageId);
        Assert.Equal("manager001", runtime.Calls[0].ExternalUserId);

        var sendRequest = handler.Requests.Single(request => request.RequestUri!.AbsolutePath.Contains("/topapi/message/corpconversation/asyncsend_v2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("access_token=dingtalk-token", sendRequest.RequestUri!.Query);
        var sendBody = await sendRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"agent_id\":123456", sendBody);
        Assert.Contains("\"userid_list\":\"manager001\"", sendBody);
        Assert.Contains("runtime reply", sendBody);
    }

    [Fact]
    public async Task HandleAsync_WithValidWeComPayload_RoutesToRuntimeConversation_AndSendsReply()
    {
        await using var dbContext = CreateDbContext();
        var aesKey = BuildWeComEncodingAesKey();
        SeedConfig(dbContext, "wecom", aesKey);

        var handler = new RecordingHttpMessageHandler(
            request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/cgi-bin/gettoken", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"errcode":0,"errmsg":"ok","access_token":"wecom-token"}""", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri!.AbsolutePath.Contains("/cgi-bin/message/send", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"errcode":0,"errmsg":"ok"}""", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found")
                };
            });

        var runtime = new FakeRuntimeConversationService();
        var service = CreateService(dbContext, runtime, handler);
        var plainXml = """
        <xml>
          <ToUserName><![CDATA[corp-id]]></ToUserName>
          <FromUserName><![CDATA[zhangsan]]></FromUserName>
          <CreateTime>1714440000</CreateTime>
          <MsgType><![CDATA[text]]></MsgType>
          <Content><![CDATA[帮我安排会议]]></Content>
          <MsgId>1234567890123</MsgId>
          <AgentID>123456</AgentID>
        </xml>
        """;
        var payload = BuildWeComEncryptedPayload(plainXml, "corp-id", aesKey, "verify", "1714440000", "nonce-1");

        var response = await service.HandleAsync(
            "wecom",
            "pc_1",
            payload.PayloadXml,
            payload.Headers);

        Assert.True(response.Success, response.Message);
        Assert.Equal("replied", response.Data!.Status);
        Assert.Single(runtime.Calls);
        Assert.Equal("wecom", runtime.Calls[0].Channel);
        Assert.Equal("帮我安排会议", runtime.Calls[0].Content);
        Assert.Equal("1234567890123", runtime.Calls[0].ExternalMessageId);
        Assert.Equal("zhangsan", runtime.Calls[0].ExternalUserId);

        var sendRequest = handler.Requests.Single(request => request.RequestUri!.AbsolutePath.Contains("/cgi-bin/message/send", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("access_token=wecom-token", sendRequest.RequestUri!.Query);
        var sendBody = await sendRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"touser\":\"zhangsan\"", sendBody);
        Assert.Contains("\"agentid\":123456", sendBody);
        Assert.Contains("runtime reply", sendBody);
    }

    [Fact]
    public async Task VerifyAsync_WithValidWeComEcho_ReturnsDecryptedEcho()
    {
        await using var dbContext = CreateDbContext();
        var aesKey = BuildWeComEncodingAesKey();
        SeedConfig(dbContext, "wecom", aesKey);
        var service = CreateService(dbContext, new FakeRuntimeConversationService());
        var echo = BuildWeComEncryptedPayload("echo-ok", "corp-id", aesKey, "verify", "1714440000", "nonce-1");

        var response = await service.VerifyAsync(
            "wecom",
            "pc_1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["msg_signature"] = echo.Signature,
                ["timestamp"] = "1714440000",
                ["nonce"] = "nonce-1",
                ["echostr"] = echo.Encrypt
            });

        Assert.True(response.Success, response.Message);
        Assert.Equal("verified", response.Data!.Status);
        Assert.Equal("echo-ok", response.Data.Reply);
    }

    private static IReadOnlyDictionary<string, string> BuildFeishuHeaders(string payload, string verificationToken)
    {
        var timestamp = "1714440000";
        var nonce = "nonce-1";
        var signature = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{verificationToken}{timestamp}{nonce}{payload}")));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Lark-Signature"] = signature,
            ["X-Lark-Request-Timestamp"] = timestamp,
            ["X-Lark-Request-Nonce"] = nonce
        };
    }

    private static string BuildWeComEncodingAesKey()
    {
        return Convert.ToBase64String(Enumerable.Range(1, 32).Select(item => (byte)item).ToArray()).TrimEnd('=');
    }

    private static WeComEncryptedPayload BuildWeComEncryptedPayload(
        string plainText,
        string corpId,
        string encodingAesKey,
        string token,
        string timestamp,
        string nonce)
    {
        var key = Convert.FromBase64String(encodingAesKey + "=");
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var corpBytes = Encoding.UTF8.GetBytes(corpId);
        var random = Encoding.UTF8.GetBytes("1234567890123456");
        var length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(plainBytes.Length));
        var combined = random.Concat(length).Concat(plainBytes).Concat(corpBytes).ToArray();
        var padded = AddPkcs7Padding(combined);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = key[..16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        var encrypted = Convert.ToBase64String(encryptor.TransformFinalBlock(padded, 0, padded.Length));
        var signature = BuildWeComSignature(token, timestamp, nonce, encrypted);
        var payloadXml = new XDocument(
            new XElement("xml", new XElement("Encrypt", new XCData(encrypted))))
            .ToString(SaveOptions.DisableFormatting);

        return new WeComEncryptedPayload(
            encrypted,
            signature,
            payloadXml,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["msg_signature"] = signature,
                ["timestamp"] = timestamp,
                ["nonce"] = nonce
            });
    }

    private static string BuildWeComSignature(string token, string timestamp, string nonce, string encrypted)
    {
        var values = new[] { token, timestamp, nonce, encrypted }.OrderBy(item => item, StringComparer.Ordinal);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Concat(values)))).ToLowerInvariant();
    }

    private static byte[] AddPkcs7Padding(byte[] bytes)
    {
        var pad = 32 - bytes.Length % 32;
        if (pad == 0)
        {
            pad = 32;
        }

        return bytes.Concat(Enumerable.Repeat((byte)pad, pad)).ToArray();
    }

    private sealed record WeComEncryptedPayload(
        string Encrypt,
        string Signature,
        string PayloadXml,
        IReadOnlyDictionary<string, string> Headers);

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private static void SeedConfig(HireBotDbContext dbContext, string platform = "feishu", string? aesKey = null)
    {
        dbContext.ImConfigs.Add(new ImConfigEntity
        {
            ConfigId = "cfg_1",
            InstanceId = "pc_1",
            TenantId = "tenant-1",
            OwnerUserId = "owner-1",
            Platform = platform,
            ConnectionMode = "url_callback",
            WebhookPath = $"/api/v1/im/{platform}/webhook/pc_1",
            VerificationToken = "protected:verify",
            AppId = "protected:app-id",
            AppSecret = "protected:app-secret",
            EncryptKey = "protected:encrypt",
            Token = "protected:verify",
            CorpId = "protected:corp-id",
            AgentId = "protected:123456",
            AgentSecret = "protected:agent-secret",
            AesKey = string.IsNullOrWhiteSpace(aesKey) ? null : $"protected:{aesKey}",
            Status = "active",
            ConfiguredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        dbContext.SaveChanges();
    }

    private static ImWebhookService CreateService(
        HireBotDbContext dbContext,
        FakeRuntimeConversationService runtime,
        RecordingHttpMessageHandler? handler = null,
        IImWebhookReplayContext? replayContext = null)
    {
        var factory = new FakeHttpClientFactory(handler ?? new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            }));

        return new ImWebhookService(
            dbContext,
            new PrefixSecretProtector(),
            runtime,
            replayContext ?? new FakeReplayContext(),
            factory,
            NullLogger<ImWebhookService>.Instance);
    }

    private sealed class FakeRuntimeConversationService : IInstanceRuntimeConversationService
    {
        public List<Call> Calls { get; } = [];

        public Task<ApiResponse<InstanceChatTimelineDto>> GetMessagesAsync(string instanceId, string channel, string? ownerUserId = null, int limit = 50, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ApiResponse<InstanceChatResultDto>> SendMessageAsync(
            string instanceId,
            string channel,
            string content,
            string? ownerUserId = null,
            string? externalMessageId = null,
            string? externalUserId = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call(instanceId, channel, content, ownerUserId, externalMessageId, externalUserId));
            var result = new InstanceChatResultDto(
                instanceId,
                "conv_1",
                new InstanceChatMessageDto("msg_1", "assistant", "runtime reply", DateTimeOffset.UtcNow));
            return Task.FromResult(ApiResponse<InstanceChatResultDto>.SuccessResponse(result));
        }

        public Task<ApiResponse<bool>> ClearMessagesAsync(string instanceId, string channel, string? ownerUserId = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public sealed record Call(
            string InstanceId,
            string Channel,
            string Content,
            string? OwnerUserId,
            string? ExternalMessageId,
            string? ExternalUserId);
    }

    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string? Protect(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"protected:{value.Trim()}";

        public string? Unprotect(string? value) => value?.StartsWith("protected:", StringComparison.Ordinal) == true ? value["protected:".Length..] : value;
    }

    private sealed class FakeReplayContext : IImWebhookReplayContext
    {
        public bool SkipOutboundSend { get; set; }

        public bool UseMockKingCrew { get; set; }

        public string? MockKingCrewReply { get; set; }

        public void Reset()
        {
            SkipOutboundSend = false;
            UseMockKingCrew = false;
            MockKingCrewReply = null;
        }
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(content, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
            }

            return clone;
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = name switch
            {
                "DingTalk" => new Uri("https://oapi.dingtalk.com"),
                "WeCom" => new Uri("https://qyapi.weixin.qq.com"),
                _ => new Uri("https://open.feishu.cn")
            }
        };
    }
}
