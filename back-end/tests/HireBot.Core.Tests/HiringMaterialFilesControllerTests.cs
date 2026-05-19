using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.ApiService.Controllers;
using HireBot.ApiService.McpTools;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class HiringMaterialFilesControllerTests
{
    [Fact]
    public async Task UploadAsync_ShouldPersistMetadataAndWriteFile()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await using var dbContext = CreateDbContext(databaseRoot);
            await SeedHiringContextAsync(dbContext, "hire-001", "session-001");
            var controller = CreateController(dbContext, root);
            var content = Encoding.UTF8.GetBytes("# 身份资料\n");

            var result = await controller.UploadAsync(
                "hire-001",
                "session-001",
                "身份材料",
                "身份证明",
                [CreateFormFile("profile.md", content, "text/markdown")],
                cancellationToken);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<ApiResponse<IReadOnlyList<HiringMaterialFilesController.HiringMaterialFileDto>>>(ok.Value);
            Assert.True(response.Success);
            Assert.Single(response.Data!);

            var dto = response.Data![0];
            var entity = await dbContext.HiringMaterialFiles.SingleAsync(cancellationToken);
            Assert.Equal("hire-001", entity.HireId);
            Assert.Equal("session-001", entity.SessionId);
            Assert.Equal("身份材料/profile.md", entity.RelativePath);
            Assert.Equal("身份证明", entity.RequestedCategoryTitle);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), entity.Sha256);
            Assert.Equal(entity.MaterialFileId, dto.MaterialFileId);
            Assert.True(File.Exists(entity.StoragePath));
            Assert.Equal("# 身份资料\n", await File.ReadAllTextAsync(entity.StoragePath, cancellationToken));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task UploadAsync_WhenSessionDoesNotMatchRuntime_ShouldReturnConflict()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await using var dbContext = CreateDbContext(databaseRoot);
            await SeedHiringContextAsync(dbContext, "hire-002", "session-current");
            var controller = CreateController(dbContext, root);

            var result = await controller.UploadAsync(
                "hire-002",
                "session-other",
                null,
                null,
                [CreateFormFile("profile.md", Encoding.UTF8.GetBytes("{}"), "application/json")],
                cancellationToken);

            var conflict = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
            Assert.Empty(dbContext.HiringMaterialFiles);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task UploadAsync_WhenSameRelativePathUploadedAgain_ShouldUpdateExistingRecord()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await using var dbContext = CreateDbContext(databaseRoot);
            await SeedHiringContextAsync(dbContext, "hire-003", "session-003");
            var controller = CreateController(dbContext, root);
            var firstContent = Encoding.UTF8.GetBytes("{\"v\":1}");
            var secondContent = Encoding.UTF8.GetBytes("{\"v\":2}");

            await controller.UploadAsync(
                "hire-003",
                "session-003",
                "流程",
                "旧分类",
                [CreateFormFile("rules.json", firstContent, "application/json")],
                cancellationToken);
            var firstEntity = await dbContext.HiringMaterialFiles.SingleAsync(cancellationToken);
            var materialFileId = firstEntity.MaterialFileId;

            await controller.UploadAsync(
                "hire-003",
                "session-003",
                "流程",
                "新分类",
                [CreateFormFile("rules.json", secondContent, "application/json")],
                cancellationToken);

            var entity = await dbContext.HiringMaterialFiles.SingleAsync(cancellationToken);
            Assert.Equal(materialFileId, entity.MaterialFileId);
            Assert.Equal("新分类", entity.RequestedCategoryTitle);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(secondContent)), entity.Sha256);
            Assert.Equal("{\"v\":2}", await File.ReadAllTextAsync(entity.StoragePath, cancellationToken));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ListAsync_ShouldReturnMaterialFilesFromDatabase()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var root = CreateTempRoot();
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await using var dbContext = CreateDbContext(databaseRoot);
            await SeedHiringContextAsync(dbContext, "hire-004", "session-004");
            var controller = CreateController(dbContext, root);
            await controller.UploadAsync(
                "hire-004",
                "session-004",
                null,
                "业务规则",
                [CreateFormFile("rules.md", Encoding.UTF8.GetBytes("# rules"), "text/markdown")],
                cancellationToken);

            var result = await controller.ListAsync("hire-004", "session-004", cancellationToken);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<ApiResponse<IReadOnlyList<HiringMaterialFilesController.HiringMaterialFileDto>>>(ok.Value);
            Assert.True(response.Success);
            var item = Assert.Single(response.Data!);
            Assert.Equal("rules.md", item.RelativePath);
            Assert.Equal("业务规则", item.RequestedCategoryTitle);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }



    private static HiringMaterialFilesController CreateController(HireBotDbContext dbContext, string root)
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        return new HiringMaterialFilesController(
            new TestWebHostEnvironment(root),
            CreateConfiguration(),
            dbContext,
            httpContextAccessor,
            NullLogger<HiringMaterialFilesController>.Instance);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("HireBot:DataRoot", "data")
            ])
            .Build();
    }

    private static HireBotDbContext CreateDbContext(InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase($"material-files-{Guid.NewGuid():N}", databaseRoot)
            .Options;
        return new HireBotDbContext(options);
    }

    private static async Task SeedHiringContextAsync(HireBotDbContext dbContext, string hireId, string sessionId)
    {
        dbContext.HiringSessions.Add(new HiringSessionEntity
        {
            SessionId = sessionId,
            HireId = hireId,
            TemplateId = "template-001",
            OwnerSubject = "owner-001",
            TenantId = "tenant-001",
            OperatorId = "operator-001",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        dbContext.HiringRuntimeStates.Add(new HiringRuntimeStateEntity
        {
            HireId = hireId,
            SessionId = sessionId,
            CurrentStage = "material",
            CollectionPhase = "in_progress",
            PayloadJson = "{}",
            PackagesJson = "{}",
            WorkflowStateJson = "{}",
            ConversationCacheJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static IFormFile CreateFormFile(string fileName, byte[] content, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hirebot-material-files-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HireBot.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string WebRootPath { get; set; } = contentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
