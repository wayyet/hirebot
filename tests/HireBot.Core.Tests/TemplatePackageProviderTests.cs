using System.Text;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Tests;

public sealed class TemplatePackageProviderTests
{
    [Fact]
    public async Task FileSystemDiscoveryRoleTemplatePackageProvider_LoadAsync_ShouldReadEntrySkillAndStageRules()
    {
        var fileSystemProvider = CreateFileSystemTemplatePackageProvider();
        var roleProvider = new FileSystemDiscoveryRoleTemplatePackageProvider(
            fileSystemProvider,
            CreateHostEnvironment(),
            CreateConfiguration());

        var package = await roleProvider.LoadAsync();

        Assert.Equal("digital-employee-discovery", package.PackageId);
        Assert.Equal("skills/employment-coach-conversation", package.EntrySkill);
        Assert.NotEmpty(package.StageRules);
        Assert.Contains(package.PackageFiles, file => file.RelativePath.Equals("config/IDENTITY.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(package.PackageFiles, file => file.RelativePath.Equals("ontology/ontology-slice.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FileSystemDiscoveryRoleTemplatePackageProvider_LoadAsync_ShouldIncludeTodoToolContract()
    {
        var fileSystemProvider = CreateFileSystemTemplatePackageProvider();
        var roleProvider = new FileSystemDiscoveryRoleTemplatePackageProvider(
            fileSystemProvider,
            CreateHostEnvironment(),
            CreateConfiguration());

        var package = await roleProvider.LoadAsync();

        var entrySkill = Assert.Single(
            package.PackageFiles,
            file => file.RelativePath.Equals(
                "skills/employment-coach-conversation/SKILL.md",
                StringComparison.OrdinalIgnoreCase));
        var entrySkillContent = Encoding.UTF8.GetString(entrySkill.Content);
        Assert.Contains("`todo` 工具", entrySkillContent);
        Assert.Contains("todo.add", entrySkillContent);
        Assert.Contains("\"kind\":\"skip\"", entrySkillContent, StringComparison.Ordinal);
        Assert.Contains("status` 写成 `confirmed`", entrySkillContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<handoff_todo_patch>", entrySkillContent, StringComparison.Ordinal);

        var todoSchema = Assert.Single(
            package.PackageFiles,
            file => file.RelativePath.Equals(
                "skills/employment-coach-conversation/references/handoff-todo-schema.md",
                StringComparison.OrdinalIgnoreCase));
        var todoSchemaContent = Encoding.UTF8.GetString(todoSchema.Content);
        Assert.Contains("todo.notes", todoSchemaContent);
        Assert.Contains("\"targetSkill\"", todoSchemaContent);
        Assert.Contains("todo.complete", todoSchemaContent);
        Assert.Contains("`status` 写成 `confirmed`", todoSchemaContent, StringComparison.Ordinal);
    }

    [Fact]
    public void CoachSystemPrompt_ShouldRequireTodoToolContract()
    {
        var apiRoot = ResolveApiServiceRoot();
        var promptPath = Path.Combine(apiRoot, "Assets", "md", "coach-system-prompt.md");

        Assert.True(File.Exists(promptPath), $"Missing prompt file: {promptPath}");

        var promptContent = File.ReadAllText(promptPath, Encoding.UTF8);
        Assert.Contains("todo.add", promptContent);
        Assert.Contains("todo.notes", promptContent);
        Assert.Contains("\"todoIds\"", promptContent);
        Assert.Contains("\"kind\":\"skip\"", promptContent, StringComparison.Ordinal);
        Assert.Contains("status` 写成 `confirmed`", promptContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<handoff_todo_patch>", promptContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileSystemDiscoveryRuleProvider_LoadAsync_ShouldBuildDiscoverySkillFromRoleTemplate()
    {
        var fileSystemProvider = CreateFileSystemTemplatePackageProvider();
        var roleProvider = new FileSystemDiscoveryRoleTemplatePackageProvider(
            fileSystemProvider,
            CreateHostEnvironment(),
            CreateConfiguration());
        var discoveryProvider = new FileSystemDiscoveryRuleProvider(roleProvider);

        var discoverySkill = await discoveryProvider.LoadAsync();

        Assert.Equal("digital-employee-discovery", discoverySkill.SkillId);
        Assert.Contains(discoverySkill.StageRules, rule =>
            string.Equals(rule.Stage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.SkillName, "ontology_extraction", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(discoverySkill.SkillContent));
    }

    [Fact]
    public async Task FileSystemWorkingTemplatePackageProvider_LoadAsync_ShouldLoadMinimalSkeleton()
    {
        var fileSystemProvider = CreateFileSystemTemplatePackageProvider();
        var provider = new FileSystemWorkingTemplatePackageProvider(
            fileSystemProvider,
            CreateHostEnvironment(),
            CreateConfiguration());

        var package = await provider.LoadAsync();

        Assert.Equal("hirebot-working-skeleton", package.PackageId);
        Assert.Contains(package.PackageFiles, file => file.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(package.PackageFiles, file => file.RelativePath.Equals("config/IDENTITY.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(package.PackageFiles, file => file.RelativePath.Equals("ontology/ontology-slice.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(package.PackageFiles, file => file.RelativePath.Equals("skills/README.md", StringComparison.OrdinalIgnoreCase));
    }

    private static FileSystemTemplatePackageProvider CreateFileSystemTemplatePackageProvider()
    {
        return new FileSystemTemplatePackageProvider(CreateHostEnvironment(), CreateConfiguration());
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder().Build();
    }

    private static IHostEnvironment CreateHostEnvironment()
    {
        return new StubHostEnvironment
        {
            ContentRootPath = ResolveApiServiceRoot()
        };
    }

    private static string ResolveApiServiceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var apiRoot = Path.Combine(current.FullName, "src", "HireBot.ApiService");
            if (Directory.Exists(Path.Combine(apiRoot, "Assets")))
            {
                return apiRoot;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate HireBot.ApiService root for template package tests.");
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "HireBot.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
