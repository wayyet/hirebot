using HireBot.Abstraction;
using HireBot.Abstraction.Services.User;
using HireBot.Core.Services;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HireBot.Core.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddHireBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Server=(localdb)\\mssqllocaldb;Database=HireBot;Trusted_Connection=True;";

        // 注册数据库上下文
        services.AddDbContext<HireBotDbContext>(options =>
                options.UseNpgsql(connectionString));

        // 注册仓储
        services.AddScoped<IHireBotRepository, HireBotRepository>();

        // 注册业务服务
        services.AddScoped<IUserService, UserService>();

        return services;
    }
}
