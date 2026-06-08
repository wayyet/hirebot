using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring;

namespace HireBot.Core.Tests;

public sealed class FinalPackageManifestUpdaterTests
{
    [Fact]
    public void AppendLinkedSkills_ShouldAppendLinkedSkillIntoManifest()
    {
        var packageFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(
                """
                {
                  "name": "demo-template",
                  "skills": [
                    {
                      "name": "live-insertion-feasibility",
                      "path": "skills/live-insertion-feasibility/SKILL.md",
                      "required": true
                    }
                  ]
                }
                """)
        };

        var result = FinalPackageManifestUpdater.AppendLinkedSkills(
            packageFiles,
            new HiringSkillLinkConfigDto
            {
                LinkedSkills =
                [
                    new HiringLinkedSkillItemDto
                    {
                        SkillId = "019ddd2a-3c5b-7ce8-977b-7d7a07d92965",
                        Name = "文档内容解析"
                    }
                ]
            });

        Assert.True(result.ManifestFound);
        Assert.True(result.Updated);
        Assert.Equal(1, result.ExistingSkillCount);
        Assert.Equal(1, result.AddedSkillCount);
        Assert.Equal(2, result.FinalSkillCount);
        Assert.Contains("skills/文档内容解析/SKILL.md", result.AddedSkillPaths);

        using var document = JsonDocument.Parse(packageFiles["manifest.json"]);
        var skills = document.RootElement.GetProperty("skills").EnumerateArray().ToArray();
        Assert.Equal(2, skills.Length);
        Assert.Contains(skills, skill =>
            skill.GetProperty("name").GetString() == "文档内容解析" &&
            skill.GetProperty("path").GetString() == "skills/文档内容解析/SKILL.md" &&
            skill.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void AppendLinkedSkills_WhenSkillAlreadyExists_ShouldNotDuplicateManifestEntry()
    {
        var packageFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(
                """
                {
                  "name": "demo-template",
                  "skills": [
                    {
                      "name": "文档内容解析",
                      "path": "skills/文档内容解析/SKILL.md",
                      "required": true
                    }
                  ]
                }
                """)
        };

        var result = FinalPackageManifestUpdater.AppendLinkedSkills(
            packageFiles,
            new HiringSkillLinkConfigDto
            {
                LinkedSkills =
                [
                    new HiringLinkedSkillItemDto
                    {
                        SkillId = "019ddd2a-3c5b-7ce8-977b-7d7a07d92965",
                        Name = "文档内容解析"
                    }
                ]
            });

        Assert.True(result.ManifestFound);
        Assert.False(result.Updated);
        Assert.Equal(1, result.ExistingSkillCount);
        Assert.Equal(0, result.AddedSkillCount);
        Assert.Equal(1, result.FinalSkillCount);

        using var document = JsonDocument.Parse(packageFiles["manifest.json"]);
        var skills = document.RootElement.GetProperty("skills").EnumerateArray().ToArray();
        Assert.Single(skills);
    }
}
