using System.Text.Json;
using HireBot.Abstraction.Infrastructure.Identity;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class EmployeeRuntimeServiceTests
{
    [Fact]
    public async Task CreateFromHireAsync_WhenHiringInstanceExists_RebindsItToNewHireId()
    {
        await using var dbContext = CreateDbContext();
        var existingEmployee = CreateEmployee("employee-1", "old-hire", "template-1");
        dbContext.Instances.Add(new InstanceEntity
        {
            InstanceId = existingEmployee.EmployeeId,
            TenantId = "default",
            InstanceType = "department",
            Status = EmployeeStatus.Hiring,
            BasedOnTemplateId = "template-1",
            HireId = "old-hire",
            OwnerUserId = "owner-1",
            DepartmentId = "default",
            CurrentVersion = "v_initial",
            RuntimeSnapshotJson = JsonSerializer.Serialize(existingEmployee),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(dbContext);
        var result = await service.CreateFromHireAsync(
            new CreateEmployeeFromHireRequestDto(
                HireId: "new-hire",
                TemplateId: "template-1",
                TemplateName: "Template One",
                Description: "Updated description",
                OwnerSubject: "owner-1",
                TenantId: "default",
                OperatorId: "operator-1",
                Capabilities: ["capability-1"]),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("employee-1", result.Data!.EmployeeId);

        var stored = await dbContext.Instances.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("new-hire", stored.HireId);
        Assert.Equal(EmployeeStatus.Hiring, stored.Status);
        Assert.Equal("template-1", stored.BasedOnTemplateId);
        Assert.Equal("Updated description", stored.Description);
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase($"employee-runtime-{Guid.NewGuid():N}")
            .Options;

        return new HireBotDbContext(options);
    }

    private static EmployeeRuntimeService CreateService(HireBotDbContext dbContext)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new EmployeeRuntimeService(
            new TestUserIdentity("owner-1", "default", "operator-1"),
            dbContext,
            null!,
            null!,
            null!,
            null!,
            null!,
            configuration,
            null!,
            null!,
            NullLogger<EmployeeRuntimeService>.Instance);
    }

    private static EmployeeDetailDto CreateEmployee(string employeeId, string hireId, string templateId)
    {
        return new EmployeeDetailDto(
            EmployeeId: employeeId,
            Nickname: "Template One",
            RoleName: "Template One",
            SourceTemplate: "Template One",
            SourceTemplateId: templateId,
            InstanceType: "department",
            Status: EmployeeStatus.Hiring,
            BasedOnTemplateId: templateId,
            FromInstanceId: null,
            OwnerUserId: "owner-1",
            DepartmentId: "default",
            LifecycleStatus: "hiring",
            StageSummary: $"Hiring flow {hireId}",
            PrimarySignal: "Waiting",
            SignalLevel: "ok",
            OwningTeam: "default",
            CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
            InternshipStartAt: null,
            GraduatedAt: null,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: [],
            Capabilities: [new EmployeeCapabilityDto("capability-1", false)],
            EvalPhase: null,
            EvalIteration: null,
            EvalMaxIterations: null,
            IsConfigured: false,
            CardIntro: null,
            Description: "Original description",
            CreatedBy: null);
    }

    private sealed class TestUserIdentity(string ownerSubject, string tenantId, string operatorId) : IUserIdentity
    {
        public string Id => ownerSubject;
        public string Email => "test@example.com";
        public string UserName => "testuser";
        public string FirstName => "Test";
        public string LastName => "User";
        public string FullName => "Test User";
        public string DisplayName => "Test User";
        public string? TenantId => tenantId;
        public string? TenantName => "Test Tenant";
        public string OperatorId => operatorId;
        public string OwnerSubject => ownerSubject;
        public string? Role => "manager";
        public bool IsAuthenticated => true;
        public string? DepartmentId => null;
    }
}
