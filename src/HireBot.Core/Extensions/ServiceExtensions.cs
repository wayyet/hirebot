using HireBot.Abstraction;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeTemplate;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.SkillCatalog;
using HireBot.Abstraction.Services.Training;
using HireBot.Abstraction.Services.User;
using HireBot.Core.Providers;
using HireBot.Core.Services;
using HireBot.Core.Services.Collaboration;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.EmployeeTemplate;
using HireBot.Core.Services.Evaluation;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.SkillCatalog;
using HireBot.Core.Services.Training;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HireBot.Core.Extensions;

public static class ServiceExtensions
{
    private const string KingCrewClientName = "KingCrew";
    private const string BuildServiceClientName = "BuildService";

    public static IServiceCollection AddHireBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ??
                               "Server=(localdb)\\mssqllocaldb;Database=HireBot;Trusted_Connection=True;";

        services.AddDbContext<HireBotDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IHireBotRepository, HireBotRepository>();

        services.AddHttpClient(KingCrewClientName, (_, client) =>
        {
            var baseUrl = configuration["KingCrew:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = uri;
            }

            var timeoutSeconds = configuration.GetValue("KingCrew:HttpTimeoutSeconds", 120);
            if (timeoutSeconds <= 0)
            {
                timeoutSeconds = 120;
            }

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        services.AddHttpClient(BuildServiceClientName, (_, client) =>
        {
            var baseUrl = configuration["BuildService:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = uri;
            }

            var timeoutSeconds = configuration.GetValue("BuildService:HttpTimeoutSeconds", 60);
            if (timeoutSeconds <= 0)
            {
                timeoutSeconds = 60;
            }

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        services.AddScoped<IRequestContextService, RequestContextService>();

        services.AddSingleton<IEmployeeRuntimeStore, InMemoryEmployeeRuntimeStore>();
        services.AddSingleton<IHiringRuntimeStore, InMemoryHiringRuntimeStore>();
        services.AddSingleton<IEvaluationScenarioProvider, MockEvaluationScenarioProvider>();
        services.AddSingleton<ICollaborationProvider, MockCollaborationProvider>();
        services.AddSingleton<ISkillCatalogProvider, MockSkillCatalogProvider>();
        services.AddSingleton<BuildServiceTemplatePackageProvider>();
        services.AddSingleton<FileSystemTemplatePackageProvider>();
        services.AddSingleton<ITemplatePackageProvider, FallbackTemplatePackageProvider>();
        services.AddSingleton<IDiscoveryRuleProvider, FileSystemDiscoveryRuleProvider>();
        services.AddSingleton<HiringStageCompletionEvaluator>();
        services.AddSingleton<IArtifactSerializer, PlaceholderArtifactSerializer>();

        services.AddScoped<IUserService, UserService>();
        services.AddSingleton<ITemplateDataProvider, BuildServiceTemplateDataProvider>();

        services.AddScoped<IEmployeeTemplateService, EmployeeTemplateService>();
        services.AddSingleton<IEmployeeHiringService, EmployeeHiringService>();
        services.AddScoped<IEmployeeRuntimeService, MockEmployeeRuntimeService>();
        services.AddScoped<ITrainingService, MockTrainingService>();
        services.AddScoped<IEvaluationService, MockEvaluationService>();
        services.AddScoped<ICollaborationService, MockCollaborationService>();
        services.AddScoped<ISkillCatalogService, MockSkillCatalogService>();

        var dataMode = configuration["HireBot:DataMode"] ?? "Mock";
        if (string.Equals(dataMode, "Real", StringComparison.OrdinalIgnoreCase))
        {
            // Keep service contracts unchanged. Real implementations can replace mock services later.
        }

        return services;
    }
}
