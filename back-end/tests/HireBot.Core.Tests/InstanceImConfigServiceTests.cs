using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HireBot.Core.Tests;

public sealed class InstanceImConfigServiceTests
{
    [Fact]
    public async Task UpsertConfigAsync_ForPersonalClone_EncryptsCredentialsAndReturnsActive()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var protector = new PrefixSecretProtector();
        var service = CreateService(dbContext, protector);

        var response = await service.UpsertConfigAsync(
            instance.InstanceId,
            "feishu",
            new ImConfigRequestDto("url_callback", "app-id", "secret", "encrypt", null, null, "verify", null, null, null));

        Assert.True(response.Success, response.Message);
        Assert.Equal("active", response.Data!.Status);
        var config = await dbContext.ImConfigs.SingleAsync();
        Assert.Equal("protected:secret", config.AppSecret);
        Assert.Equal("protected:encrypt", config.EncryptKey);
        Assert.Equal("protected:verify", config.VerificationToken);
        Assert.Equal($"/api/v1/im/feishu/webhook/{instance.InstanceId}", config.WebhookPath);
    }

    [Fact]
    public async Task UpsertConfigAsync_ForDepartmentInstance_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "department", "live", "owner-1");
        var service = CreateService(dbContext);

        var response = await service.UpsertConfigAsync(
            instance.InstanceId,
            "feishu",
            new ImConfigRequestDto("websocket", "app-id", "secret", null, null, null, null, null, null, null));

        Assert.False(response.Success);
        Assert.Equal(409, response.Code);
        Assert.Empty(dbContext.ImConfigs);
    }

    [Fact]
    public async Task UpsertConfigAsync_ForOtherOwner_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "someone-else");
        var service = CreateService(dbContext);

        var response = await service.UpsertConfigAsync(
            instance.InstanceId,
            "feishu",
            new ImConfigRequestDto("websocket", "app-id", "secret", null, null, null, null, null, null, null));

        Assert.False(response.Success);
        Assert.Equal(403, response.Code);
    }

    [Fact]
    public async Task GetConfigsAsync_ReturnsUnconfiguredPlatformsWhenMissing()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var service = CreateService(dbContext);

        var response = await service.GetConfigsAsync(instance.InstanceId);

        Assert.True(response.Success, response.Message);
        Assert.Equal(3, response.Data!.Configs.Count);
        Assert.Contains(response.Data.Configs, item => item.Platform == "feishu" && item.Status == "unconfigured");
        Assert.Contains(response.Data.Configs, item => item.Platform == "dingtalk" && item.Status == "unconfigured");
        Assert.Contains(response.Data.Configs, item => item.Platform == "wecom" && item.Status == "unconfigured");
    }

    [Fact]
    public async Task GetConfigsAsync_ReturnsDecryptedCredentialsForConfiguredPlatform()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var service = CreateService(dbContext);

        await service.UpsertConfigAsync(
            instance.InstanceId,
            "feishu",
            new ImConfigRequestDto("url_callback", "app-id", "secret", "encrypt", null, null, "verify", null, null, null));

        var response = await service.GetConfigsAsync(instance.InstanceId);

        Assert.True(response.Success, response.Message);
        var config = Assert.Single(response.Data!.Configs, item => item.Platform == "feishu");
        Assert.Equal("app-id", config.AppId);
        Assert.Equal("secret", config.AppSecret);
        Assert.Equal("encrypt", config.EncryptKey);
        Assert.Equal("verify", config.VerificationToken);
    }

    [Fact]
    public async Task DeleteConfigAsync_RemovesPlatformConfig()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var service = CreateService(dbContext);
        await service.UpsertConfigAsync(
            instance.InstanceId,
            "feishu",
            new ImConfigRequestDto("websocket", "app-id", "secret", null, null, null, null, null, null, null));

        var response = await service.DeleteConfigAsync(instance.InstanceId, "feishu");

        Assert.True(response.Success, response.Message);
        Assert.Empty(dbContext.ImConfigs);
    }

    [Fact]
    public async Task UpsertConfigAsync_ForWecom_RequiresCallbackCredentials()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var service = CreateService(dbContext);

        var response = await service.UpsertConfigAsync(
            instance.InstanceId,
            "wecom",
            new ImConfigRequestDto("url_callback", null, null, null, "token", "aes", null, "corp", "agent", "secret"));

        Assert.True(response.Success, response.Message);
        var config = await dbContext.ImConfigs.SingleAsync();
        Assert.Equal("protected:corp", config.CorpId);
        Assert.Equal("protected:agent", config.AgentId);
        Assert.Equal("protected:secret", config.AgentSecret);
    }

    [Fact]
    public async Task UpsertConfigAsync_ForWecom_WebsocketMode_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var service = CreateService(dbContext);

        var response = await service.UpsertConfigAsync(
            instance.InstanceId,
            "wecom",
            new ImConfigRequestDto("websocket", "app-id", "secret", null, null, null, null, null, null, null));

        Assert.False(response.Success);
        Assert.Equal(400, response.Code);
        Assert.Contains("企业微信仅支持 url_callback 模式", response.Message);
        Assert.Empty(dbContext.ImConfigs);
    }

    [Fact]
    public async Task UpsertConfigAsync_ForDingTalk_RequiresAgentId()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var service = CreateService(dbContext);

        var response = await service.UpsertConfigAsync(
            instance.InstanceId,
            "dingtalk",
            new ImConfigRequestDto("url_callback", "app-id", "secret", "encrypt", null, null, null, null, null, null));

        Assert.False(response.Success);
        Assert.Equal(400, response.Code);
        Assert.Contains("agent_id", response.Message);
        Assert.Empty(dbContext.ImConfigs);
    }

    [Fact]
    public async Task UpsertConfigAsync_ForDingTalk_StoresAgentId()
    {
        await using var dbContext = CreateDbContext();
        var instance = SeedInstance(dbContext, "personal_clone", "live", "owner-1");
        var service = CreateService(dbContext);

        var response = await service.UpsertConfigAsync(
            instance.InstanceId,
            "dingtalk",
            new ImConfigRequestDto("url_callback", "app-id", "secret", "encrypt", null, null, null, null, "123456", null));

        Assert.True(response.Success, response.Message);
        var config = await dbContext.ImConfigs.SingleAsync();
        Assert.Equal("protected:123456", config.AgentId);
    }

    private static InstanceImConfigService CreateService(
        HireBotDbContext dbContext,
        ISecretProtector? secretProtector = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HireBot:PublicBaseUrl"] = "http://localhost:5280"
            })
            .Build();

        return new InstanceImConfigService(
            dbContext,
            new FakeRequestContextService("owner-1"),
            secretProtector ?? new PrefixSecretProtector(),
            configuration,
            new HttpContextAccessor());
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private static InstanceEntity SeedInstance(HireBotDbContext dbContext, string type, string status, string owner)
    {
        var instance = new InstanceEntity
        {
            InstanceId = $"inst_{Guid.NewGuid():N}",
            TenantId = "tenant-1",
            InstanceType = type,
            Status = status,
            BasedOnTemplateId = "tpl-1",
            FromInstanceId = type == "personal_clone" ? "dept_1" : null,
            EvalReportId = null,
            OwnerUserId = owner,
            DepartmentId = "dept",
            CurrentVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Instances.Add(instance);
        dbContext.SaveChanges();
        return instance;
    }

    private sealed class FakeRequestContextService(string ownerSubject) : IRequestContextService
    {
        public string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null) => ownerSubject;

        public (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
        {
            return (tenantId ?? "tenant-1", operatorId ?? "operator-1");
        }
    }

    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string? Protect(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"protected:{value.Trim()}";

        public string? Unprotect(string? value) => value?.StartsWith("protected:", StringComparison.Ordinal) == true ? value["protected:".Length..] : value;
    }
}

