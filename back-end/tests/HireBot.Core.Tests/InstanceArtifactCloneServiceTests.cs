using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Tests;

public sealed class InstanceArtifactCloneServiceTests
{
    private sealed class TestHostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Directory.GetCurrentDirectory());
    }
    [Fact]
    public async Task CloneArtifactsAsync_ShouldFallbackToTemplatePackage_WhenSourceHasOnlyMetadata()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hirebot-artifact-clone-tests", Guid.NewGuid().ToString("N"));
        var artifactRoot = Path.Combine(tempRoot, "artifacts");
        var templateRoot = Path.Combine(tempRoot, "templates");
        Directory.CreateDirectory(artifactRoot);
        Directory.CreateDirectory(templateRoot);

        try
        {
            CreateMetadataOnlySourcePackage(artifactRoot);
            CreateTemplatePackage(templateRoot);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("HireBot:ArtifactStoreRoot", artifactRoot),
                    new KeyValuePair<string, string?>("HireBot:TemplatePackagesRoot", templateRoot),
                    new KeyValuePair<string, string?>("HireBot:DataRoot", "data"),
                    new KeyValuePair<string, string?>("HireBot:PersonalCloneArtifactsRoot", "personal-clone-artifacts")
                ])
                .Build();

            await using var dbContext = CreateDbContext();
            dbContext.Instances.Add(new Repository.Entities.InstanceEntity
            {
                InstanceId = "source-001",
                TenantId = "tenant-a",
                InstanceType = "department",
                Status = "live",
                BasedOnTemplateId = "sales-coach",
                FromInstanceId = null,
                EvalReportId = null,
                OwnerUserId = "owner-a",
                DepartmentId = "tenant-a",
                CurrentVersion = "v_meta",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();

            var service = new InstanceArtifactCloneService(
                configuration,
                new TestHostingEnvironment
                {
                    ContentRootPath = tempRoot,
                    ContentRootFileProvider = new PhysicalFileProvider(tempRoot)
                },
                dbContext);
            var source = BuildEmployee("source-001", "sales-coach");

            var result = await service.CloneArtifactsAsync(source, "clone-001");

            Assert.NotEmpty(result.CopiedFiles);
            Assert.Contains("manifest.json", result.CopiedFiles);
            Assert.Contains("README.md", result.CopiedFiles);
            Assert.Contains(Path.Combine("skills", "pipeline-qualifier", "SKILL.md").Replace('\\', '/'), result.CopiedFiles);

            var copiedFile = Path.Combine(result.TargetRootPath, "manifest.json");
            Assert.True(File.Exists(copiedFile));
            Assert.Equal("{\"name\":\"template\"}", await File.ReadAllTextAsync(copiedFile));
            Assert.StartsWith(
                Path.Combine(tempRoot, "data", "personal-clone-artifacts", "source-001", "clone-001"),
                result.TargetRootPath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CloneArtifactsAsync_ShouldReadSourceFromDigitalWorkforceRoot_AndStoreCloneUnderDataRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hirebot-artifact-clone-tests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(tempRoot, "data", "digital-workforce", "source-001");
        Directory.CreateDirectory(sourceRoot);
        File.WriteAllText(Path.Combine(sourceRoot, "manifest.json"), "{\"name\":\"source\"}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("HireBot:DataRoot", "data"),
                    new KeyValuePair<string, string?>("HireBot:DigitalWorkforceRoot", "digital-workforce"),
                    new KeyValuePair<string, string?>("HireBot:PersonalCloneArtifactsRoot", "personal-clone-artifacts")
                ])
                .Build();

            await using var dbContext = CreateDbContext();
            var service = new InstanceArtifactCloneService(
                configuration,
                new TestHostingEnvironment
                {
                    ContentRootPath = tempRoot,
                    ContentRootFileProvider = new PhysicalFileProvider(tempRoot)
                },
                dbContext);

            var result = await service.CloneArtifactsAsync(BuildEmployee("source-001", "sales-coach"), "clone-001");

            Assert.Contains("manifest.json", result.CopiedFiles);
            Assert.Equal("{\"name\":\"source\"}", await File.ReadAllTextAsync(Path.Combine(result.TargetRootPath, "manifest.json")));
            Assert.StartsWith(
                Path.Combine(tempRoot, "data", "personal-clone-artifacts", "source-001", "clone-001"),
                result.TargetRootPath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallbackToDigitalWorkforceRoot_WhenDepartmentArtifactsAreMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hirebot-artifact-resolver-tests", Guid.NewGuid().ToString("N"));
        var digitalWorkforceRoot = Path.Combine(tempRoot, "data", "digital-workforce");
        var departmentRoot = Path.Combine(digitalWorkforceRoot, "dept-001");
        Directory.CreateDirectory(departmentRoot);
        File.WriteAllText(Path.Combine(departmentRoot, "manifest.json"), "{\"name\":\"department\"}");

        try
        {
            var resolver = CreateResolver(tempRoot);
            var result = await resolver.ResolveAsync(new Repository.Entities.InstanceEntity
            {
                InstanceId = "dept-001",
                TenantId = "tenant-a",
                InstanceType = "department",
                Status = "live",
                OwnerUserId = "owner-a",
                DepartmentId = "tenant-a",
                CurrentVersion = "v_missing",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.Equal(departmentRoot, result.ArtifactRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldUsePersonalCloneArtifactsRoot_ForCloneRehire()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hirebot-artifact-resolver-tests", Guid.NewGuid().ToString("N"));
        var cloneRoot = Path.Combine(
            tempRoot,
            "data",
            "personal-clone-artifacts",
            "dept-001",
            "pc-001",
            "versions",
            "v_clone");
        Directory.CreateDirectory(cloneRoot);
        File.WriteAllText(Path.Combine(cloneRoot, "manifest.json"), "{\"name\":\"clone\"}");

        try
        {
            var resolver = CreateResolver(tempRoot);
            var result = await resolver.ResolveAsync(new Repository.Entities.InstanceEntity
            {
                InstanceId = "pc-001",
                TenantId = "tenant-a",
                InstanceType = "personal_clone",
                Status = "retired",
                BasedOnTemplateId = "sales-coach",
                FromInstanceId = "dept-001",
                OwnerUserId = "owner-a",
                DepartmentId = "tenant-a",
                CurrentVersion = "v_clone",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.Equal(cloneRoot, result.ArtifactRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallbackToSourceDigitalWorkforceRoot_ForRetiredCloneRehire()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hirebot-artifact-resolver-tests", Guid.NewGuid().ToString("N"));
        var digitalWorkforceRoot = Path.Combine(tempRoot, "data", "digital-workforce");
        var sourceRoot = Path.Combine(digitalWorkforceRoot, "dept-001");
        Directory.CreateDirectory(sourceRoot);
        File.WriteAllText(Path.Combine(sourceRoot, "manifest.json"), "{\"name\":\"source\"}");

        try
        {
            var resolver = CreateResolver(tempRoot);
            var result = await resolver.ResolveAsync(new Repository.Entities.InstanceEntity
            {
                InstanceId = "pc-001",
                TenantId = "tenant-a",
                InstanceType = "personal_clone",
                Status = "retired",
                BasedOnTemplateId = "sales-coach",
                FromInstanceId = "dept-001",
                OwnerUserId = "owner-a",
                DepartmentId = "tenant-a",
                CurrentVersion = "v_missing",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            Assert.Equal(sourceRoot, result.ArtifactRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static InstanceArtifactResolver CreateResolver(string contentRootPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("HireBot:DataRoot", "data"),
                new KeyValuePair<string, string?>("HireBot:DigitalWorkforceRoot", "digital-workforce"),
                new KeyValuePair<string, string?>("HireBot:PersonalCloneArtifactsRoot", "personal-clone-artifacts")
            ])
            .Build();

        return new InstanceArtifactResolver(
            configuration,
            new TestHostingEnvironment
            {
                ContentRootPath = contentRootPath,
                ContentRootFileProvider = new PhysicalFileProvider(contentRootPath)
            });
    }

    private static void CreateMetadataOnlySourcePackage(string artifactRoot)
    {
        var sourceRoot = Path.Combine(artifactRoot, "instances", "department", "source-001", "versions", "v_meta");
        Directory.CreateDirectory(sourceRoot);
        File.WriteAllText(Path.Combine(sourceRoot, "instance.json"), "{\"employeeId\":\"source-001\"}");
        File.WriteAllText(Path.Combine(sourceRoot, "manifest.json"), "{\"name\":\"source\"}");
    }

    private static void CreateTemplatePackage(string templateRoot)
    {
        var packageRoot = Path.Combine(templateRoot, "sales-coach");
        Directory.CreateDirectory(Path.Combine(packageRoot, "skills", "pipeline-qualifier"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "ontology"));
        File.WriteAllText(Path.Combine(packageRoot, "manifest.json"), "{\"name\":\"template\"}");
        File.WriteAllText(Path.Combine(packageRoot, "README.md"), "template readme");
        File.WriteAllText(Path.Combine(packageRoot, "ontology", "sales-discovery-slice.md"), "slice");
        File.WriteAllText(Path.Combine(packageRoot, "skills", "pipeline-qualifier", "SKILL.md"), "skill");
    }

    private static EmployeeDetailDto BuildEmployee(string employeeId, string templateId)
    {
        return new EmployeeDetailDto(
            employeeId,
            "Sales Coach",
            "Sales Coach",
            "Sales Coach",
            templateId,
            "department",
            "live",
            templateId,
            null,
            "tenant-a",
            "tenant-a",
            "已上岗",
            "summary",
            "ok",
            "ok",
            "tenant-a",
            "2026-04-30",
            null,
            null,
            0,
            0,
            null,
            [],
            [new EmployeeCapabilityDto("站内对话", true)],
            null,
            null,
            null,
            true);
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new HireBotDbContext(options);
    }
}
