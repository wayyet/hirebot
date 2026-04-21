using HireBot.Abstraction;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeTemplate;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.User;
using HireBot.Core.Providers;
using HireBot.Core.Services.EmployeeTemplate;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HireBot.Core.Extensions;

public static class ServiceExtensions
{
    private const string KingCrabClientName = "KingCrabConsole";

    public static IServiceCollection AddHireBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Server=(localdb)\\mssqllocaldb;Database=HireBot;Trusted_Connection=True;";

        // 注册数据库上下文
        services.AddDbContext<HireBotDbContext>(options =>
                options.UseNpgsql(connectionString));

        // 注册仓储
        services.AddScoped<IHireBotRepository, HireBotRepository>();

        // KingCrab 控制台接口（用于实例生命周期管理）
        services.AddHttpClient(KingCrabClientName, (serviceProvider, client) =>
        {
            var baseUrl = configuration["KingCrabConsole:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = uri;
            }

            var timeoutSeconds = configuration.GetValue("KingCrabConsole:HttpTimeoutSeconds", 15);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        // 注册业务服务
        services.AddScoped<IUserService, UserService>();
        services.AddSingleton<ITemplateDataProvider, MockTemplateDataProvider>();
        services.AddScoped<IEmployeeTemplateService, EmployeeTemplateService>();
        services.AddSingleton<IEmployeeHiringService, EmployeeHiringService>();

        return services;
    }
}
