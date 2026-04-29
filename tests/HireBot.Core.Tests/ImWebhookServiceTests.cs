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
    public async Task HandleAsync_WithValidFeishuPayload_RoutesToRuntimeConversation()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext);
        var runtime = new FakeRuntimeConversationService();
        var service = new ImWebhookService(
            dbContext,
            new PrefixSecretProtector(),
            runtime,
            NullLogger<ImWebhookService>.Instance);

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            """
            {
              "event": {
                "sender": { "sender_id": { "open_id": "ou_1" } },
                "message": {
                  "message_id": "im_1",
                  "chat_type": "p2p",
                  "content": "{\"text\":\"帮我总结\"}"
                }
              }
            }
            """,
            new Dictionary<string, string>
            {
                ["X-HireBot-Verification-Token"] = "verify"
            });

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
    }

    [Fact]
    public async Task HandleAsync_WithInvalidToken_ReturnsUnauthorized()
    {
        await using var dbContext = CreateDbContext();
        SeedConfig(dbContext);
        var runtime = new FakeRuntimeConversationService();
        var service = new ImWebhookService(
            dbContext,
            new PrefixSecretProtector(),
            runtime,
            NullLogger<ImWebhookService>.Instance);

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            """{"message_id":"im_1","content":"hello"}""",
            new Dictionary<string, string>
            {
                ["X-HireBot-Verification-Token"] = "wrong"
            });

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
        var service = new ImWebhookService(
            dbContext,
            new PrefixSecretProtector(),
            runtime,
            NullLogger<ImWebhookService>.Instance);

        var response = await service.HandleAsync(
            "feishu",
            "pc_1",
            """{"message_id":"im_1","chat_type":"group","content":"hello"}""",
            new Dictionary<string, string>
            {
                ["X-HireBot-Verification-Token"] = "verify"
            });

        Assert.True(response.Success, response.Message);
        Assert.Equal("ignored", response.Data!.Status);
        Assert.Empty(runtime.Calls);
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private static void SeedConfig(HireBotDbContext dbContext)
    {
        dbContext.ImConfigs.Add(new ImConfigEntity
        {
            ConfigId = "cfg_1",
            InstanceId = "pc_1",
            TenantId = "tenant-1",
            OwnerUserId = "owner-1",
            Platform = "feishu",
            ConnectionMode = "url_callback",
            WebhookPath = "/api/v1/im/feishu/webhook/pc_1",
            VerificationToken = "protected:verify",
            Status = "active",
            ConfiguredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        dbContext.SaveChanges();
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
}

