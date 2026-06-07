using System.Reflection;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class EmployeeRuntimeServiceTests
{
    [Fact]
    public async Task CreateFromHireAsync_WhenSameTemplateHasExistingInterningEmployee_CreatesNewHiringInstance()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Instances.Add(new InstanceEntity
        {
            InstanceId = "existing-employee",
            TenantId = "tenant-1",
            InstanceType = "department",
            Status = "interning_ai",
            BasedOnTemplateId = "template-1",
            FromInstanceId = null,
            EvalReportId = null,
            OwnerUserId = "owner-1",
            DepartmentId = "tenant-1",
            CurrentVersion = "v_existing",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(dbContext, "owner-1");
        var result = await service.CreateFromHireAsync(
            new CreateEmployeeFromHireRequestDto(
                HireId: "hire-1",
                TemplateId: "template-1",
                TemplateName: "模板员工",
                Description: "测试模板",
                OwnerSubject: "owner-1",
                TenantId: "tenant-1",
                OperatorId: "operator-1",
                Capabilities: ["能力A"]),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.NotEqual("existing-employee", result.Data.EmployeeId);

        var created = await dbContext.Instances
            .SingleAsync(item => item.InstanceId == result.Data.EmployeeId, TestContext.Current.CancellationToken);
        Assert.Equal("hiring", created.Status);
        Assert.Equal("owner-1", created.OwnerUserId);
        Assert.Equal("tenant-1", created.TenantId);
        Assert.Equal("template-1", created.BasedOnTemplateId);
        Assert.Equal(2, await dbContext.Instances.CountAsync(TestContext.Current.CancellationToken));
    }

    private static EmployeeRuntimeService CreateService(HireBotDbContext dbContext, string ownerSubject)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new EmployeeRuntimeService(
            new TestRequestContextService(ownerSubject),
            dbContext,
            ThrowingProxy<IInstanceArtifactCloneService>.Create(),
            ThrowingProxy<IInstanceArtifactResolver>.Create(),
            ThrowingProxy<ISandboxService>.Create(),
            ThrowingProxy<IKingCrabHttpClient>.Create(),
            configuration,
            new TestHostEnvironment(),
            new NoopSecretProtector(),
            NullLogger<EmployeeRuntimeService>.Instance);
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }

    private sealed class TestRequestContextService(string ownerSubject) : IRequestContextService
    {
        public string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null) => ownerSubject;

        public (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId) =>
            (tenantId ?? "tenant-1", operatorId ?? "operator-1");
    }

    private sealed class NoopSecretProtector : ISecretProtector
    {
        public string? Protect(string? value) => value;

        public string? Unprotect(string? value) => value;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "HireBot.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private class ThrowingProxy<T> : DispatchProxy where T : class
    {
        public static T Create() => DispatchProxy.Create<T, ThrowingProxy<T>>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new NotSupportedException(targetMethod?.Name ?? typeof(T).Name);
        }
    }
}
