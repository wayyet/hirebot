using System.Text;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Tests;

public class PackagingTestCaseTemplateSnapshotBuilderTests
{
    [Fact]
    public void Build_ShouldPrioritizeManifestAndSkillFiles()
    {
        var packageFiles = new[]
        {
            CreateFile("ontology/visitor.slice.json", """{"name":"visitor"}"""),
            CreateFile("manifest.json", """{"name":"Visitor Experience Pilot"}"""),
            CreateFile("skills/visitor-orchestrator/SKILL.md", "# Visitor Skill"),
            CreateFile("assets/logo.png", [0x89, 0x50, 0x4E, 0x47])
        };

        var snapshots = PackagingTestCaseTemplateSnapshotBuilder.Build(packageFiles);

        Assert.Equal(3, snapshots.Count);
        Assert.Equal("manifest.json", snapshots[0].RelativePath);
        Assert.StartsWith("skills/", snapshots[1].RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("ontology/", snapshots[2].RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsTextSnapshotCandidate_ShouldRejectBinaryExtension()
    {
        Assert.False(PackagingTestCaseTemplateSnapshotBuilder.IsTextSnapshotCandidate("assets/archive.zip"));
        Assert.True(PackagingTestCaseTemplateSnapshotBuilder.IsTextSnapshotCandidate("config/SOUL.md"));
    }

    [Fact]
    public void Build_FromVisitorExperienceFixture_ShouldIncludeCorePaths()
    {
        var fixtureRoot = ResolveVisitorFixtureRoot();
        if (!Directory.Exists(fixtureRoot))
        {
            return;
        }

        var packageFiles = Directory
            .EnumerateFiles(fixtureRoot, "*.*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relativePath = Path.GetRelativePath(fixtureRoot, path).Replace('\\', '/');
                return CreateFile(relativePath, File.ReadAllBytes(path));
            })
            .ToArray();

        var snapshots = PackagingTestCaseTemplateSnapshotBuilder.Build(packageFiles);

        Assert.Contains(snapshots, item => item.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshots, item => item.RelativePath.Contains("SKILL.md", StringComparison.OrdinalIgnoreCase));
    }

    private static TemplatePackageFileAsset CreateFile(string relativePath, string content) =>
        CreateFile(relativePath, Encoding.UTF8.GetBytes(content));

    private static TemplatePackageFileAsset CreateFile(string relativePath, byte[] content) =>
        new(relativePath, content, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)));

    private static string ResolveVisitorFixtureRoot()
    {
        var zipPath = @"c:\Users\wayye\Documents\1.ncrew\测试\visitor-experience-pilot-artifacts\visitor-experience-pilot-artifacts.zip";
        if (!File.Exists(zipPath))
        {
            return string.Empty;
        }

        var extractRoot = Path.Combine(Path.GetTempPath(), "hirebot-visitor-fixture", Guid.NewGuid().ToString("N"));
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractRoot);
        return extractRoot;
    }
}
