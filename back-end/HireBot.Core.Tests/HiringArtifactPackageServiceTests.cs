using System.IO.Compression;
using System.Text;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class HiringArtifactPackageServiceTests
{
    [Fact]
    public async Task PersistIntermediatePackageAsync_ShouldStoreArchiveAndExposeLatestPackage()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"hiring-artifact-packages-{Guid.NewGuid():N}";
        var artifactRoot = CreateArtifactRoot();

        try
        {
            using var dbContext = CreateDbContext(databaseName, databaseRoot);
            dbContext.HiringSessions.Add(CreateHiringSessionEntity("hire-001", "session-001"));
            await dbContext.SaveChangesAsync();

            var service = CreateService(dbContext, artifactRoot);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["manifest.json"] = Encoding.UTF8.GetBytes("{\"name\":\"pkg\"}"),
                ["testcases/evaluation-test-cases.json"] = Encoding.UTF8.GetBytes("{\"cases\":[{\"case_id\":\"tc-001\"}]}")
            };

            var persistResult = await service.PersistIntermediatePackageAsync(
                new HiringArtifactPackagePersistRequestDto(
                    "hire-001",
                    "session-001",
                    "hire-001_intermediate_package.zip",
                    files));

            var latestPackage = await service.GetLatestPackageAsync("hire-001");

            Assert.NotNull(latestPackage);
            Assert.Equal(HiringArtifactPackageKinds.IntermediatePackageZip, persistResult.Kind);
            Assert.Equal(HiringArtifactPackageKinds.IntermediatePackageZip, latestPackage!.Kind);
            Assert.Equal("hire-001_intermediate_package.zip", latestPackage.FileName);

            using var archive = new ZipArchive(new MemoryStream(latestPackage.Content), ZipArchiveMode.Read);
            var entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("manifest.json", entries);
            Assert.Contains("testcases/evaluation-test-cases.json", entries);
        }
        finally
        {
            DeleteArtifactRoot(artifactRoot);
        }
    }

    [Fact]
    public async Task PersistFinalPackageAsync_ShouldSupportArchiveAndFileDownloadAcrossServiceRestart()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"hiring-artifact-packages-{Guid.NewGuid():N}";
        var artifactRoot = CreateArtifactRoot();

        try
        {
            using (var firstContext = CreateDbContext(databaseName, databaseRoot))
            {
                firstContext.HiringSessions.Add(CreateHiringSessionEntity("hire-002", "session-002"));
                await firstContext.SaveChangesAsync();

                var firstService = CreateService(firstContext, artifactRoot);
                await firstService.PersistFinalPackageAsync(
                    new HiringArtifactPackagePersistRequestDto(
                        "hire-002",
                        "session-002",
                        "hire-002_final_package.zip",
                        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["ontology/employee.json"] = Encoding.UTF8.GetBytes("{\"name\":\"guardian\"}"),
                            ["testcases/connectivity_test.json"] = Encoding.UTF8.GetBytes("{\"test_cases\":[]}")
                        }));
            }

            using var secondContext = CreateDbContext(databaseName, databaseRoot);
            var secondService = CreateService(secondContext, artifactRoot);

            var archiveDownload = await secondService.BuildFinalPackageDownloadAsync("hire-002");
            var fileDownload = await secondService.BuildFinalPackageFileDownloadAsync("hire-002", "ontology/employee.json");

            Assert.True(archiveDownload.Found);
            Assert.Equal("hire-002_final_package.zip", archiveDownload.FileName);
            Assert.NotNull(archiveDownload.Content);
            Assert.True(archiveDownload.Content!.Length > 0);

            Assert.True(fileDownload.Found);
            Assert.Equal("employee.json", fileDownload.FileName);
            Assert.Equal("application/json", fileDownload.ContentType);
            Assert.Equal("{\"name\":\"guardian\"}", Encoding.UTF8.GetString(fileDownload.Content!));
        }
        finally
        {
            DeleteArtifactRoot(artifactRoot);
        }
    }

    [Fact]
    public async Task BuildFinalPackageDownloadAsync_WhenOnlyIntermediateExists_ShouldReturnConflict()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"hiring-artifact-packages-{Guid.NewGuid():N}";
        var artifactRoot = CreateArtifactRoot();
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            using var dbContext = CreateDbContext(databaseName, databaseRoot);
            dbContext.HiringSessions.Add(CreateHiringSessionEntity("hire-003", "session-003"));
            await dbContext.SaveChangesAsync(cancellationToken);

            var service = CreateService(dbContext, artifactRoot);
            await service.PersistIntermediatePackageAsync(
                new HiringArtifactPackagePersistRequestDto(
                    "hire-003",
                    "session-003",
                    "hire-003_intermediate_package.zip",
                    new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["manifest.json"] = Encoding.UTF8.GetBytes("{\"name\":\"intermediate\"}")
                    }),
                cancellationToken);

            var download = await service.BuildFinalPackageDownloadAsync("hire-003", cancellationToken);

            Assert.False(download.Found);
            Assert.Equal(409, download.Code);
        }
        finally
        {
            DeleteArtifactRoot(artifactRoot);
        }
    }

    private static HiringArtifactPackageService CreateService(HireBotDbContext dbContext, string storeRoot)
    {
        return new HiringArtifactPackageService(
            dbContext,
            new FileSystemFileStore(storeRoot),
            NullLogger<HiringArtifactPackageService>.Instance);
    }

    private static HireBotDbContext CreateDbContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new HireBotDbContext(options);
    }

    private static HiringSessionEntity CreateHiringSessionEntity(string hireId, string sessionId)
    {
        return new HiringSessionEntity
        {
            SessionId = sessionId,
            HireId = hireId,
            TemplateId = "default",
            OwnerSubject = "tenant-1:operator-1",
            TenantId = "tenant-1",
            OperatorId = "operator-1",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 创建临时存储根目录（包含 artifact-store 子目录）。
    /// 返回的路径为 storeRoot（父目录），测试验证时 artifactRoot = storeRoot/artifact-store。
    /// </summary>
    private static string CreateArtifactRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "hirebot-artifact-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "artifact-store"));
        return path;
    }

    private static void DeleteArtifactRoot(string storeRoot)
    {
        if (Directory.Exists(storeRoot))
        {
            Directory.Delete(storeRoot, recursive: true);
        }
    }
}

/// <summary>
/// 测试用的 IHostEnvironment 存根，ContentRootPath 指向指定目录。
/// </summary>
file sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
