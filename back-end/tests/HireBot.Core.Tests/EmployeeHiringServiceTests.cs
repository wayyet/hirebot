using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Security;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Sandbox;

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
    public void ApplyConversationProgressToTemplatePackage_WhenTestCasesGeneratedStatus_ShouldAppendEvaluationTestCases()
    {
        var templatePackage = CreateTemplatePackage();
        var runtimeContext = CreateRuntimeContext(templatePackage) with
        {
            CurrentStage = "systems",
            PackagingTestCasesStatus = PackagingTestCasesGenerationStatuses.Generated,
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
    public void ApplyConversationProgressToTemplatePackage_WithExternalSystemConfig_ShouldWriteExternalArtifacts()
    {
        var templatePackage = CreateTemplatePackage();
        var runtimeContext = CreateRuntimeContext(templatePackage) with
        {
            ExternalSystemConfig = new HiringExternalSystemConfigState(
                SubmissionMode: HiringExternalSystemSubmissionModes.Configured,
                CliTools:
                [
                    new HiringCliToolConfigState(
                        Name: "jq",
                        Command: "jq",
                        Description: "处理 JSON",
                        ExecutionMode: "sandbox",
                        Parameters: null)
                ],
                McpServer: new HiringMcpServerConfigState(
                    Transport: "http",
                    Name: "CRM MCP",
                    Command: string.Empty,
                    Args: [],
                    ProtectedEnv: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["API_KEY"] = "protected-secret"
                    },
                    EnvPassThrough: [],
                    Cwd: string.Empty,
                    Url: "https://mcp.example.com",
                    BearerTokenEnv: string.Empty,
                    ProtectedHeaders: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    HeadersFromEnv: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-05-26T08:00:00Z"))
        };

        var updated = EmployeeHiringService.ApplyConversationProgressToTemplatePackage(runtimeContext);
        var files = updated.WorkingTemplatePackage.PackageFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("external/user-config.json", files.Keys);
        Assert.Contains("external/external-config.index.json", files.Keys);
        Assert.Contains("external/systems/cli.json", files.Keys);
        Assert.Contains("external/systems/mcp.json", files.Keys);
        Assert.Contains("external/README.md", files.Keys);

        var userConfigJson = Encoding.UTF8.GetString(files["external/user-config.json"].Content);
        var indexJson = Encoding.UTF8.GetString(files["external/external-config.index.json"].Content);
        var readme = Encoding.UTF8.GetString(files["external/README.md"].Content);

        Assert.Contains("jq", userConfigJson);
        Assert.Contains("protected_literal", userConfigJson);
        Assert.Contains("protected-secret", userConfigJson);
        Assert.Contains("external/systems/mcp.json", indexJson);
        Assert.Contains("API Key: 已通过安全存储绑定", readme);
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
            Skills: [],
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

    [Fact]
    public void BuildMergedMcpConfig_WithUserHttpMcp_MergesCorrectly()
    {
        var protector = new FakeSecretProtector();
        var globalConfig = CreateGlobalMcpConfig();
        var externalConfig = CreateConfiguredExternalConfig(
            new HiringMcpServerConfigState(
                Transport: "http",
                Name: "CRM MCP",
                Command: string.Empty,
                Args: [],
                ProtectedEnv: EmptyMap(),
                EnvPassThrough: [],
                Cwd: string.Empty,
                Url: "https://mcp.example.com",
                BearerTokenEnv: string.Empty,
                ProtectedHeaders: EmptyMap(),
                HeadersFromEnv: EmptyMap()));

        var merged = EmployeeHiringService.BuildMergedMcpConfig(globalConfig, externalConfig, protector);

        Assert.True(merged.Enabled);
        Assert.Equal(2, merged.Servers.Count);
        Assert.True(merged.Servers.ContainsKey("weather"));

        // 用户 server 使用由 SanitizeServerId 生成的同名 Key
        Assert.True(merged.Servers.ContainsKey("crmmcp"));
        var userServer = merged.Servers["crmmcp"];
        Assert.Equal("streamable-http", userServer.Transport);
        Assert.Equal("https://mcp.example.com", userServer.Url);
        Assert.Equal("crmmcp.", userServer.ToolNamePrefix);
        Assert.True(userServer.Enabled);
    }

    [Fact]
    public void BuildMergedMcpConfig_WithUserStdioMcp_MergesCorrectly()
    {
        var protector = new FakeSecretProtector();
        var globalConfig = CreateGlobalMcpConfig();
        var protectedEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // FakeSecretProtector 使用 protected: 前缀模拟加密
            ["API_KEY"] = "protected:secret-token"
        };
        var externalConfig = CreateConfiguredExternalConfig(
            new HiringMcpServerConfigState(
                Transport: "stdio",
                Name: "local-tool",
                Command: "node",
                Args: ["server.js", "--port", "3000"],
                ProtectedEnv: protectedEnv,
                EnvPassThrough: [],
                Cwd: "/workspace/tool",
                Url: string.Empty,
                BearerTokenEnv: string.Empty,
                ProtectedHeaders: EmptyMap(),
                HeadersFromEnv: EmptyMap()));

        var merged = EmployeeHiringService.BuildMergedMcpConfig(globalConfig, externalConfig, protector);

        Assert.True(merged.Servers.ContainsKey("local-tool"));
        var entry = merged.Servers["local-tool"];
        Assert.Equal("stdio", entry.Transport);
        Assert.Equal("node", entry.Command);
        Assert.NotNull(entry.Arguments);
        Assert.Equal(new[] { "server.js", "--port", "3000" }, entry.Arguments);
        Assert.Equal("/workspace/tool", entry.WorkingDirectory);
        Assert.NotNull(entry.Environment);
        Assert.Equal("secret-token", entry.Environment!["API_KEY"]);
        // stdio 模式不应填充 Url/Headers
        Assert.Equal(string.Empty, entry.Url);
        Assert.Null(entry.Headers);
    }

    [Fact]
    public void BuildMergedMcpConfig_WithNullExternalConfig_ReturnsGlobalOnly()
    {
        var protector = new FakeSecretProtector();
        var globalConfig = CreateGlobalMcpConfig();

        var merged = EmployeeHiringService.BuildMergedMcpConfig(globalConfig, externalConfig: null, protector);

        Assert.True(merged.Enabled);
        Assert.Single(merged.Servers);
        Assert.True(merged.Servers.ContainsKey("weather"));
    }

    [Fact]
    public void BuildMergedMcpConfig_DecryptsProtectedValues()
    {
        var protector = new FakeSecretProtector();
        var protectedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Tenant"] = "protected:tenant-007"
        };
        var externalConfig = CreateConfiguredExternalConfig(
            new HiringMcpServerConfigState(
                Transport: "http",
                Name: "secure-mcp",
                Command: string.Empty,
                Args: [],
                ProtectedEnv: EmptyMap(),
                EnvPassThrough: [],
                Cwd: string.Empty,
                Url: "https://secure.example.com",
                BearerTokenEnv: "protected:bearer-xyz",
                ProtectedHeaders: protectedHeaders,
                HeadersFromEnv: EmptyMap()));

        var merged = EmployeeHiringService.BuildMergedMcpConfig(globalConfig: null, externalConfig, protector);

        var entry = Assert.Single(merged.Servers).Value;
        Assert.NotNull(entry.Headers);
        Assert.Equal("tenant-007", entry.Headers!["X-Tenant"]);
        Assert.Equal("Bearer bearer-xyz", entry.Headers!["Authorization"]);
    }

    [Fact]
    public void BuildMergedMcpConfig_SkippedMode_ExcludesUserMcp()
    {
        var protector = new FakeSecretProtector();
        var globalConfig = CreateGlobalMcpConfig();
        // skipped 模式：SubmissionMode=skipped、McpServer=null，IsPersisted=true 但 HasAnyConfig=false
        var skippedExternalConfig = new HiringExternalSystemConfigState(
            SubmissionMode: HiringExternalSystemSubmissionModes.Skipped,
            CliTools: [],
            McpServer: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        Assert.True(skippedExternalConfig.IsPersisted);

        var merged = EmployeeHiringService.BuildMergedMcpConfig(globalConfig, skippedExternalConfig, protector);

        Assert.Single(merged.Servers);
        Assert.True(merged.Servers.ContainsKey("weather"));
        // 用户 MCP 未被合并
        Assert.DoesNotContain(merged.Servers.Keys, key => key.StartsWith("user", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("我的服务器!@#", "user-mcp")] // 中文与特殊字符全部被过滤
    [InlineData("", "user-mcp")]
    [InlineData("   ", "user-mcp")]
    [InlineData("My-Server_01", "my-server_01")] // 保留字母数字与连字符/下划线且小写化
    [InlineData("--leading--", "leading")] // 修剪首尾占位符
    public void SanitizeServerId_WithSpecialCharacters_ReturnsCleanId(string input, string expected)
    {
        Assert.Equal(expected, EmployeeHiringService.SanitizeServerId(input));
    }

    private static SandboxWorkspaceMcpConfig CreateGlobalMcpConfig()
    {
        return new SandboxWorkspaceMcpConfig
        {
            Enabled = true,
            Servers = new Dictionary<string, SandboxMcpServerEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["weather"] = new SandboxMcpServerEntry
                {
                    Transport = "streamable-http",
                    Url = "https://global.example.com/weather",
                    Enabled = true,
                    ToolNamePrefix = "weather."
                }
            }
        };
    }

    private static HiringExternalSystemConfigState CreateConfiguredExternalConfig(HiringMcpServerConfigState mcpServer)
    {
        return new HiringExternalSystemConfigState(
            SubmissionMode: HiringExternalSystemSubmissionModes.Configured,
            CliTools: [],
            McpServer: mcpServer,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, string> EmptyMap()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 测试用加解密实现：使用 "protected:" 前缀标记加密以便验证 Unprotect 被调用。
    /// 未带前缀的输入原样返回，与生产环境 ISecretProtector 可能返回空串的行为不同，
    /// 但在合并逻辑测试中只关心“加密走 Unprotect”路径。
    /// </summary>
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string? Protect(string? value) => value is null ? null : $"protected:{value}";

        public string? Unprotect(string? value)
        {
            if (value is null)
            {
                return null;
            }

            const string prefix = "protected:";
            return value.StartsWith(prefix, StringComparison.Ordinal)
                ? value[prefix.Length..]
                : value;
        }
    }
}
