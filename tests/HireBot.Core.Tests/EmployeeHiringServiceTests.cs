using System.IO.Compression;
using System.Text;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Tests;

public class EmployeeHiringServiceTests
{
    [Fact]
    public void BuildDigitalEmployeeArchive_ShouldContainTemplateAndDiscoveryFiles()
    {
        var templatePackage = new TemplatePackageDefinition(
            RequestedTemplateId: "default",
            PackageId: "pkg",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            PackageRootPath: "Assets/TemplatePackages/default/NCrewTemplate",
            ManifestJson: "{\"name\":\"pkg\"}",
            DisplayName: "pkg",
            Description: "desc",
            PackageFiles:
            [
                new TemplatePackageFileAsset("manifest.json", Encoding.UTF8.GetBytes("{\"name\":\"pkg\"}"), "h1"),
                new TemplatePackageFileAsset("skills/spec-generation/SKILL.md", Encoding.UTF8.GetBytes("# skill"), "h2"),
            ],
            OntologySlices: [],
            RequiredSkills: []);

        var discoverySkill = new DiscoverySkillDefinition(
            SkillId: "digital-employee-discovery",
            SkillVersion: "1.0.0",
            SkillHash: "hash",
            SkillRootPath: "Assets/SystemSkills/digital-employee-discovery",
            SkillContent: "# discovery",
            Files:
            [
                new DiscoverySkillFileAsset("SKILL.md", "# discovery", "h3")
            ],
            StageRules: []);

        var bytes = EmployeeHiringService.BuildDigitalEmployeeArchive(templatePackage, discoverySkill);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("manifest.json", entries);
        Assert.Contains("skills/spec-generation/SKILL.md", entries);
        Assert.Contains("skills/digital-employee-discovery/SKILL.md", entries);
    }

    [Fact]
    public void ApplyConversationProgressToTemplatePackage_ShouldUpsertSnapshotFiles()
    {
        var templatePackage = new TemplatePackageDefinition(
            RequestedTemplateId: "default",
            PackageId: "pkg",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            PackageRootPath: "Assets/TemplatePackages/default/NCrewTemplate",
            ManifestJson: "{\"name\":\"pkg\"}",
            DisplayName: "pkg",
            Description: "desc",
            PackageFiles: [],
            OntologySlices: [],
            RequiredSkills: []);

        var runtimeContext = new HiringRuntimeContext
        {
            HireId = "hire-1",
            TemplateId = "default",
            TemplateName = "template",
            OwnerSubject = "owner",
            TenantId = "tenant",
            OperatorId = "operator",
            SandboxId = "sandbox",
            CurrentStage = "goal",
            CollectionPhase = "in_progress",
            TemplatePackage = templatePackage,
            DiscoverySkill = new DiscoverySkillDefinition(
                SkillId: "digital-employee-discovery",
                SkillVersion: "1.0.0",
                SkillHash: "hash",
                SkillRootPath: "Assets/SystemSkills/digital-employee-discovery",
                SkillContent: "# discovery",
                Files: [],
                StageRules: []),
            StructuredData = new Dictionary<string, string?>
            {
                ["goal"] = "提升成交率"
            },
            Materials =
            [
                new HiringConversationMaterialDto
                {
                    Type = "text",
                    Name = "conversation",
                    Content = "用户补充了业务背景"
                }
            ],
            StageCompletion = []
        };

        var updated = EmployeeHiringService.ApplyConversationProgressToTemplatePackage(runtimeContext);

        var files = updated.TemplatePackage.PackageFiles.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ontology/hiring-session/structured-data.json", files.Keys);
        Assert.Contains("ontology/hiring-session/materials.json", files.Keys);

        var structuredContent = Encoding.UTF8.GetString(files["ontology/hiring-session/structured-data.json"].Content);
        var materialContent = Encoding.UTF8.GetString(files["ontology/hiring-session/materials.json"].Content);
        Assert.Contains("goal", structuredContent);
        Assert.Contains("conversation", materialContent);
    }
}
