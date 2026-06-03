using System.Text;
using HireBot.Repository;
using HireBot.Repository.Entities;
using HireBot.Core.Services.Hiring;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Core.Tests;

public class PackagingTestCaseMaterialLoaderTests
{
    [Fact]
    public async Task LoadAsync_WhenMaterialFilesExist_ShouldReturnTruncatedSnapshots()
    {
        var dbContext = CreateDbContext();
        var tempDir = Path.Combine(Path.GetTempPath(), "hirebot-material-loader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var storagePath = Path.Combine(tempDir, "rules.md");
        await File.WriteAllTextAsync(storagePath, new string('访', 9000), Encoding.UTF8);

        dbContext.HiringMaterialFiles.Add(new HiringMaterialFileEntity
        {
            HireId = "hire-1",
            SessionId = "session-1",
            RelativePath = "rules.md",
            OriginalFileName = "rules.md",
            StoragePath = storagePath,
            Format = "md",
            Sha256 = "abc",
            RequestedCategoryTitle = "访客预约与审核规则",
            TenantId = "tenant",
            OperatorId = "operator",
            UploadedBy = "user"
        });
        await dbContext.SaveChangesAsync();

        var snapshots = await PackagingTestCaseMaterialLoader.LoadAsync(
            dbContext,
            "hire-1",
            "session-1",
            [tempDir],
            CancellationToken.None);

        Assert.Single(snapshots);
        Assert.Equal("访客预约与审核规则", snapshots[0].RequestedCategoryTitle);
        Assert.Equal(PackagingTestCaseMaterialLoader.MaxSingleFileCharacters, snapshots[0].Content.Length);
    }

    [Fact]
    public async Task LoadAsync_WhenStoragePathOutsideAllowedRoot_ShouldSkip()
    {
        var dbContext = CreateDbContext();
        var allowedRoot = Path.Combine(Path.GetTempPath(), "hirebot-material-loader-allowed", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "hirebot-material-loader-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        var storagePath = Path.Combine(outsideRoot, "rules.md");
        await File.WriteAllTextAsync(storagePath, "# 规则", Encoding.UTF8);

        dbContext.HiringMaterialFiles.Add(new HiringMaterialFileEntity
        {
            HireId = "hire-1",
            SessionId = "session-1",
            RelativePath = "rules.md",
            OriginalFileName = "rules.md",
            StoragePath = storagePath,
            Format = "md",
            Sha256 = "abc",
            RequestedCategoryTitle = "访客预约与审核规则",
            TenantId = "tenant",
            OperatorId = "operator",
            UploadedBy = "user"
        });
        await dbContext.SaveChangesAsync();

        var snapshots = await PackagingTestCaseMaterialLoader.LoadAsync(
            dbContext,
            "hire-1",
            "session-1",
            [allowedRoot],
            CancellationToken.None);

        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task LoadAsync_WhenNoRecords_ShouldReturnEmpty()
    {
        var dbContext = CreateDbContext();

        var snapshots = await PackagingTestCaseMaterialLoader.LoadAsync(
            dbContext,
            "hire-empty",
            "session-empty",
            [Path.GetTempPath()],
            CancellationToken.None);

        Assert.Empty(snapshots);
    }

    private static HireBotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HireBotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HireBotDbContext(options);
    }
}
