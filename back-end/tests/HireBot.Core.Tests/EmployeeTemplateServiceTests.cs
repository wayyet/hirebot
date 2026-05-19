using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using HireBot.Core.Services.EmployeeTemplate;
using HireBot.Core.Services.Hiring.TemplatePackages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public sealed class EmployeeTemplateServiceTests
{
    [Fact]
    public async Task GetTemplateDetailAsync_ShouldIncludeSkillsFromTargetTemplatePackage()
    {
        var template = new EmployeeTemplateDefinition(
            TemplateId: "sales-coach",
            IconUrl: "https://example.com/icon.png",
            Name: "销售教练",
            Tagline: "帮助销售梳理机会",
            Description: "desc",
            DetailDoc: "doc",
            CoreAbilityTags: ["sales"],
            HiredCount: 1,
            SuccessRate: 0m,
            AvgRating: 0m,
            IsAvailable: true,
            CoreAbilities: ["机会判断"],
            InScope: ["销售"],
            OutOfScope: [],
            Prerequisites: [],
            SuccessCases: []);
        var package = new TemplatePackageDefinition(
            RequestedTemplateId: "sales-coach",
            PackageId: "sales-coach",
            PackageVersion: "1.0.0",
            PackageHash: "hash",
            SourceArchive: null,
            PackageRootPath: "pkg-root",
            ManifestJson: "{}",
            DisplayName: "sales-coach",
            Description: "desc",
            PackageFiles: [],
            OntologySlices: [],
            Skills:
            [
                new TemplateSkillAsset("pipeline-qualifier", "skills/pipeline-qualifier/SKILL.md", true, "# skill", "h1"),
                new TemplateSkillAsset("bridge-to-forge", "skills/bridge-to-forge/SKILL.md", false, "# skill", "h2")
            ],
            RequiredSkills:
            [
                new TemplateSkillAsset("pipeline-qualifier", "skills/pipeline-qualifier/SKILL.md", true, "# skill", "h1")
            ],
            EntrySkill: "skills/pipeline-qualifier",
            StageRules: []);

        var service = new EmployeeTemplateService(
            new StubTemplateDataProvider(template),
            new StubTemplatePackageProvider(package),
            NullLogger<EmployeeTemplateService>.Instance);

        var result = await service.GetTemplateDetailAsync("sales-coach");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.PackageSkills.Count);
        Assert.Contains(result.Data.PackageSkills, skill => skill.Name == "pipeline-qualifier" && skill.Required);
        Assert.Contains(result.Data.PackageSkills, skill => skill.Name == "bridge-to-forge" && !skill.Required);
    }

    [Fact]
    public async Task FileSystemTemplatePackageProvider_ShouldKeepOptionalSkillsInManifest()
    {
        var provider = new FileSystemTemplatePackageProvider(
            new TestHostingEnvironment(),
            new ConfigurationBuilder().Build());
        var packageRoot = Path.Combine(FindBackendRoot(), "src", "HireBot.ApiService", "Assets", "TemplatePackages", "default");

        var package = await provider.LoadFromDirectoryAsync(packageRoot, "default");

        Assert.True(package.Skills.Count > package.RequiredSkills.Count);
        Assert.Contains(package.Skills, skill => skill.Name == "bridge-to-forge" && !skill.Required);
        Assert.Contains(package.RequiredSkills, skill => skill.Name == "context-priming" && skill.Required);
    }

    private static string FindBackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "HireBot.ApiService", "Assets", "TemplatePackages");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate back-end root from test base directory.");
    }

    private sealed class StubTemplateDataProvider(EmployeeTemplateDefinition template) : ITemplateDataProvider
    {
        public Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmployeeTemplateDefinition>>([template]);

        public Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
            => Task.FromResult<EmployeeTemplateDefinition?>(template);
    }

    private sealed class StubTemplatePackageProvider(TemplatePackageDefinition package) : ITemplatePackageProvider
    {
        public Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default)
            => Task.FromResult(package);
    }

    private sealed class TestHostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "HireBot.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
