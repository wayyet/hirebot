using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
            SourceArchive: null,
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
            SourceArchive: null,
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

    [Fact]
    public void ApplyConversationProgressToTemplatePackage_WithSkillArchiveMaterial_ShouldAppendEvaluationTestCases()
    {
        var templatePackage = new TemplatePackageDefinition(
            RequestedTemplateId: "default",
            PackageId: "pkg",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            SourceArchive: null,
            PackageRootPath: "Assets/TemplatePackages/default/NCrewTemplate",
            ManifestJson: "{\"name\":\"pkg\"}",
            DisplayName: "pkg",
            Description: "desc",
            PackageFiles: [],
            OntologySlices: [],
            RequiredSkills: []);

        var runtimeContext = new HiringRuntimeContext
        {
            HireId = "hire-2",
            TemplateId = "default",
            TemplateName = "template",
            OwnerSubject = "owner",
            TenantId = "tenant",
            OperatorId = "operator",
            SandboxId = "sandbox",
            CurrentStage = "systems",
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
                ["business_goal"] = "提升客户服务闭环效率",
                ["user_profile"] = "客服团队"
            },
            Materials =
            [
                new HiringConversationMaterialDto
                {
                    Type = "skill",
                    Name = "evaluation-expert.zip",
                    Content = CreateEvaluationSkillArchiveBase64(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["skillName"] = "evaluation-expert",
                        ["description"] = "严格评估判分",
                        ["archiveFormat"] = "zip",
                        ["contentEncoding"] = "base64"
                    }
                }
            ],
            StageCompletion = []
        };

        var updated = EmployeeHiringService.ApplyConversationProgressToTemplatePackage(runtimeContext);
        var files = updated.TemplatePackage.PackageFiles.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("ontology/hiring-session/evaluation-test-cases.json", files.Keys);
        var testCasesJson = Encoding.UTF8.GetString(files["ontology/hiring-session/evaluation-test-cases.json"].Content);
        using var testCasesDocument = JsonDocument.Parse(testCasesJson);
        var root = testCasesDocument.RootElement;
        var firstCaseTitle = root.GetProperty("cases")[0].GetProperty("title").GetString();
        var skillSummary = root.GetProperty("skillSummary").GetString();

        Assert.Contains("eval-case-001", testCasesJson);
        Assert.Equal("conversation-skill-guided", root.GetProperty("source").GetString());
        Assert.Contains("提升客户服务闭环效率", firstCaseTitle);
        Assert.Contains("evaluation-expert", testCasesJson);
        Assert.Contains("红线原则", skillSummary);
    }

    private static string CreateEvaluationSkillArchiveBase64()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("evaluator/SKILL.md");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine("# evaluator");
            writer.WriteLine("红线原则：必须工具调用不遗漏");
        }

        return Convert.ToBase64String(memory.ToArray());
    }
}
