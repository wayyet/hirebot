using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HireBot.Core.Tests;

public sealed class InstanceArtifactCloneServiceTests
{
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
                    new KeyValuePair<string, string?>("HireBot:TemplatePackagesRoot", templateRoot)
                ])
                .Build();

            await using var dbContext = CreateDbContext();
            dbContext.Instances.Add(new Repository.Entities.InstanceEntity
            {
                InstanceId = "source-001",
                TenantId = "tenant-a",
                InstanceType = "department",
                Status = "live",
                ViaQuickClone = false,
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

            var service = new InstanceArtifactCloneService(configuration, dbContext);
            var source = BuildEmployee("source-001", "sales-coach");

            var result = await service.CloneArtifactsAsync(source, "clone-001");

            Assert.NotEmpty(result.CopiedFiles);
            Assert.Contains("manifest.json", result.CopiedFiles);
            Assert.Contains("README.md", result.CopiedFiles);
            Assert.Contains(Path.Combine("skills", "pipeline-qualifier", "SKILL.md").Replace('\\', '/'), result.CopiedFiles);

            var copiedFile = Path.Combine(result.TargetRootPath, "manifest.json");
            Assert.True(File.Exists(copiedFile));
            Assert.Equal("{\"name\":\"template\"}", await File.ReadAllTextAsync(copiedFile));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
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
