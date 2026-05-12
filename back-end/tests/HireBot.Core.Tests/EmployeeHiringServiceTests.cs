using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Tests;

public class EmployeeHiringServiceTests
{
    [Fact]
    public void BuildDigitalEmployeeArchive_ShouldContainOnlyTemplateFiles()
    {
        var templatePackage = CreateTemplatePackage(
            packageFiles:
            [
                new TemplatePackageFileAsset("manifest.json", Encoding.UTF8.GetBytes("{\"name\":\"pkg\"}"), "h1"),
                new TemplatePackageFileAsset("skills/spec-generation/SKILL.md", Encoding.UTF8.GetBytes("# skill"), "h2")
            ]);

        var bytes = EmployeeHiringService.BuildDigitalEmployeeArchive(templatePackage);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entries = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("manifest.json", entries);
        Assert.Contains("skills/spec-generation/SKILL.md", entries);
        Assert.DoesNotContain("skills/employment-coach-conversation/SKILL.md", entries);
    }

    [Fact]
    public void ApplyConversationProgressToTemplatePackage_ShouldUpsertSnapshotFiles()
    {
        var templatePackage = CreateTemplatePackage();
        var runtimeContext = CreateRuntimeContext(templatePackage) with
        {
            StructuredData = new Dictionary<string, string?>
            {
                ["goal"] = "提升成交效率"
            },
            Materials =
            [
                new HiringConversationMaterialDto
                {
                    Type = "text",
                    Name = "conversation",
                    Content = "用户补充了业务背景"
                }
            ]
        };

        var updated = EmployeeHiringService.ApplyConversationProgressToTemplatePackage(runtimeContext);

        var files = updated.WorkingTemplatePackage.PackageFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
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
        var templatePackage = CreateTemplatePackage();
        var runtimeContext = CreateRuntimeContext(templatePackage) with
        {
            CurrentStage = "systems",
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
                        ["description"] = "严格评估打分",
                        ["archiveFormat"] = "zip",
                        ["contentEncoding"] = "base64"
                    }
                }
            ]
        };

        var updated = EmployeeHiringService.ApplyConversationProgressToTemplatePackage(runtimeContext);
        var files = updated.WorkingTemplatePackage.PackageFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("testcases/evaluation-test-cases.json", files.Keys);
        Assert.Contains("ontology/hiring-session/evaluation-test-cases.json", files.Keys);

        var testCasesJson = Encoding.UTF8.GetString(files["testcases/evaluation-test-cases.json"].Content);
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

    [Fact]
    public void BuildReferenceTemplatePrimingContent_ShouldInlineSummaryAndForbidAskingForAttachmentContent()
    {
        var template = new EmployeeTemplateDefinition(
            TemplateId: "employment-coach",
            IconUrl: "https://example.com/icon.png",
            Name: "雇佣教练",
            Tagline: "帮你把模板配上岗",
            Description: "引导用户完成资料、技能和外部能力配置。",
            DetailDoc: "## 详细说明",
            CoreAbilityTags: ["流程引导"],
            HiredCount: 12,
            SuccessRate: 0.97m,
            AvgRating: 4.8m,
            IsAvailable: true,
            CoreAbilities: ["资料归类", "技能拆解"],
            InScope: ["雇佣流程"],
            OutOfScope: ["直接编写业务代码"],
            Prerequisites: [],
            SuccessCases: ["帮助客服团队整理退款流程"]);

        var templatePackage = CreateTemplatePackage();
        var content = EmployeeHiringService.BuildReferenceTemplatePrimingContent(
            template,
            templatePackage,
            "你是雇佣流程助手。");

        Assert.Contains("参考模板摘要", content, StringComparison.Ordinal);
        Assert.Contains("模板 ID: employment-coach", content, StringComparison.Ordinal);
        Assert.Contains("模板名称: 雇佣教练", content, StringComparison.Ordinal);
        Assert.Contains("不要让用户重复提供你已经收到的资料内容", content, StringComparison.Ordinal);
    }

    private static HiringRuntimeContext CreateRuntimeContext(TemplatePackageDefinition templatePackage)
    {
        return new HiringRuntimeContext
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
            ReferenceTemplatePackage = templatePackage,
            RoleTemplatePackage = templatePackage,
            WorkingTemplatePackage = templatePackage,
            DiscoverySkill = new DiscoverySkillDefinition(
                SkillId: "employment-coach-conversation",
                SkillVersion: "1.0.0",
                SkillHash: "hash",
                SkillRootPath: "Assets/DigitalEmployeeTemplates/employment-coach-conversation",
                SkillContent: "# discovery",
                Files: [],
                StageRules: []),
            StructuredData = new Dictionary<string, string?>(),
            Materials = [],
            StageCompletion = []
        };
    }

    private static TemplatePackageDefinition CreateTemplatePackage(
        IReadOnlyList<TemplatePackageFileAsset>? packageFiles = null)
    {
        return new TemplatePackageDefinition(
            RequestedTemplateId: "default",
            PackageId: "pkg",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            SourceArchive: null,
            PackageRootPath: "Assets/TemplatePackages/default/NCrewTemplate",
            ManifestJson: "{\"name\":\"pkg\"}",
            DisplayName: "pkg",
            Description: "desc",
            PackageFiles: packageFiles ?? [],
            OntologySlices: [],
            RequiredSkills: [],
            EntrySkill: null,
            StageRules: []);
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
