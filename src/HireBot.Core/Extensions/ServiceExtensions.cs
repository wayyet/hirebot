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
using HireBot.Core.Services.Collaboration;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.EmployeeTemplate;
using HireBot.Core.Services.Evaluation;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services;
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

    public static IServiceCollection AddHireBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Server=(localdb)\\mssqllocaldb;Database=HireBot;Trusted_Connection=True;";

        // 注册数据库上下文
        services.AddDbContext<HireBotDbContext>(options =>
                options.UseNpgsql(connectionString));

        // 注册仓储
        services.AddScoped<IHireBotRepository, HireBotRepository>();

        // KingCrew 网关接口（用于模板雇佣与对话运行时）
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

        // 注册请求上下文解析
        services.AddScoped<IRequestContextService, RequestContextService>();

        // 注册数据端口（默认 mock）
        services.AddSingleton<IEmployeeRuntimeStore, InMemoryEmployeeRuntimeStore>();
        services.AddSingleton<IEvaluationScenarioProvider, MockEvaluationScenarioProvider>();
        services.AddSingleton<ICollaborationProvider, MockCollaborationProvider>();
        services.AddSingleton<ISkillCatalogProvider, MockSkillCatalogProvider>();

        // 注册业务服务
        services.AddScoped<IUserService, UserService>();
        services.AddSingleton<ITemplateDataProvider, MockTemplateDataProvider>();
        services.AddScoped<IEmployeeTemplateService, EmployeeTemplateService>();
        services.AddSingleton<IEmployeeHiringService, EmployeeHiringService>();
        services.AddScoped<IEmployeeRuntimeService, MockEmployeeRuntimeService>();
        services.AddScoped<ITrainingService, MockTrainingService>();
        services.AddScoped<IEvaluationService, MockEvaluationService>();
        services.AddScoped<ICollaborationService, MockCollaborationService>();
        services.AddScoped<ISkillCatalogService, MockSkillCatalogService>();

        // 预留 DataMode=Real 的替换入口
        var dataMode = configuration["HireBot:DataMode"] ?? "Mock";
        if (dataMode.Equals("Real", StringComparison.OrdinalIgnoreCase))
        {
            // TODO: 切换为真实实现时仅替换上面的端口/服务注入，不改 Controller 与前端契约。
        }

        return services;
    }
}
