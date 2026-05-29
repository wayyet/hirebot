using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Tests;

public class PackagingTestCasesTests
{
    [Fact]
    public void BuildPackagingTestCasesPlaceholderJson_ShouldContainEmptyTestCasesAndFallbackSource()
    {
        var json = EmployeeHiringService.BuildPackagingTestCasesPlaceholderJson();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("packaging-fallback", root.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("test_cases").ValueKind);
        Assert.Equal(0, root.GetProperty("test_cases").GetArrayLength());
        Assert.True(root.TryGetProperty("generated_at", out _));
        Assert.False(root.TryGetProperty("cases", out _));
    }

    [Fact]
    public void ShouldStagePackagingTestCases_WhenReadyForPackaging_ShouldReturnTrue()
    {
        var context = CreateRuntimeContext() with { CurrentStage = HiringCollectionStage.ReadyForPackaging };

        Assert.True(EmployeeHiringService.ShouldStagePackagingTestCases(context, userMessage: null));
        Assert.True(EmployeeHiringService.ShouldStagePackagingTestCases(context, "普通消息"));
    }

    [Theory]
    [InlineData("三个阶段均已确认完成，请开始生成产物包")]
    [InlineData("请生成实例包")]
    [InlineData("开始打包")]
    public void ShouldStagePackagingTestCases_WhenPackagingIntentInMessage_ShouldReturnTrue(string message)
    {
        var context = CreateRuntimeContext() with { CurrentStage = HiringCollectionStage.Skill };

        Assert.True(EmployeeHiringService.ShouldStagePackagingTestCases(context, message));
    }

    [Fact]
    public void ShouldStagePackagingTestCases_WhenNormalMaterialMessage_ShouldReturnFalse()
    {
        var context = CreateRuntimeContext() with { CurrentStage = HiringCollectionStage.Material };

        Assert.False(EmployeeHiringService.ShouldStagePackagingTestCases(context, "请补充业务背景资料"));
    }

    [Fact]
    public void BuildPackagingTestCaseUploadRequest_ShouldTargetTestcasesDirectory()
    {
        var context = CreateRuntimeContext();
        var content = Encoding.UTF8.GetBytes(EmployeeHiringService.BuildPackagingTestCasesPlaceholderJson());

        var request = EmployeeHiringService.BuildPackagingTestCaseUploadRequest(context, content);

        Assert.Equal(SandboxScopeTypes.Hire, request.ScopeType);
        Assert.Equal(context.HireId, request.ScopeKey);
        Assert.Equal("hiring", request.SandboxRole);
        Assert.Equal("testcases", request.TargetDir);
        Assert.Equal("evaluation-test-cases.json", request.FileName);
        Assert.Equal("application/json", request.ContentType);
    }

    [Fact]
    public void ApplyConversationProgressToTemplatePackage_WhenPackagingTestCasesStaged_ShouldNotOverwriteHistoryTestCases()
    {
        var historyJson = """
            {
              "description": "history",
              "role": "digital_employee",
              "industry": "general",
              "source": "kingcrab-history-llm",
              "test_cases": [
                {
                  "test_case_id": "TC-001",
                  "scenario_name": "历史场景",
                  "input": { "user_request": "历史用户请求", "context": {} },
                  "expected_behavior_sequence": [
                    { "step": 1, "action": "回复", "criteria": "准确" }
                  ],
                  "expected_output": {
                    "resolution": "完成",
                    "user_satisfaction": "满意",
                    "artifacts_created": []
                  }
                }
              ]
            }
            """;

        var templatePackage = CreateTemplatePackage();
        var existingFiles = templatePackage.PackageFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        EmployeeHiringService.UpsertPackageFile(existingFiles, "testcases/evaluation-test-cases.json", historyJson);
        var packageWithHistory = templatePackage with { PackageFiles = existingFiles.Values.ToArray() };

        var runtimeContext = CreateRuntimeContext(packageWithHistory) with
        {
            PackagingTestCasesStaged = true,
            Materials =
            [
                new HiringConversationMaterialDto
                {
                    Type = "skill",
                    Name = "evaluation-expert.zip",
                    Content = "skill-content",
                    Metadata = new Dictionary<string, string>
                    {
                        ["skillName"] = "evaluation-expert",
                        ["description"] = "评估技能"
                    }
                }
            ]
        };

        var updated = EmployeeHiringService.ApplyConversationProgressToTemplatePackage(runtimeContext);
        var files = updated.WorkingTemplatePackage.PackageFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var testCasesJson = Encoding.UTF8.GetString(files["testcases/evaluation-test-cases.json"].Content);

        Assert.Contains("kingcrab-history-llm", testCasesJson);
        Assert.DoesNotContain("conversation-skill-guided", testCasesJson);
        Assert.Contains("TC-001", testCasesJson);
    }

    private static TemplatePackageDefinition CreateTemplatePackage()
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
            PackageFiles: [],
            OntologySlices: [],
            Skills: [],
            RequiredSkills: [],
            EntrySkill: null,
            StageRules: []);
    }

    private static HiringRuntimeContext CreateRuntimeContext(TemplatePackageDefinition? templatePackage = null)
    {
        templatePackage ??= CreateTemplatePackage();

        return new HiringRuntimeContext
        {
            HireId = "hire-packaging-test",
            TemplateId = "default",
            TemplateName = "template",
            OwnerSubject = "owner",
            TenantId = "tenant",
            OperatorId = "operator",
            SandboxId = "sandbox",
            SessionId = "session-001",
            CurrentStage = HiringCollectionStage.Material,
            CollectionPhase = "in_progress",
            RoleTemplatePackage = templatePackage,
            WorkingTemplatePackage = templatePackage,
            DiscoverySkill = new DiscoverySkillDefinition(
                SkillId: "employment-coach-conversation",
                SkillVersion: "1.0.0",
                SkillHash: "hash",
                SkillRootPath: "Assets/DigitalEmployeeTemplates/employment-coach-conversation",
                SkillContent: "# discovery",
                Files: [],
                StageRules: [])
        };
    }
}
