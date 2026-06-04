using HireBot.Abstraction;
using HireBot.Abstraction.Infrastructure.Multitenancy;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeTemplate;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Infrastructure.Multitenancy;
using HireBot.Core.Providers;
using Microsoft.Extensions.Logging;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.EmployeeTemplate;
using HireBot.Core.Services.Evaluation;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.StoreSkills;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Security;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Abstraction.Services.Security;
using HireBot.Repository;
using HireBot.Repository.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HireBot.Core.Extensions;

public static class ServiceExtensions
{
    private const string KingCrabClientName = "KingCrab";
    private const string BuildServiceClientName = "BuildService";
    private const string FeishuClientName = "Feishu";
    private const string DingTalkClientName = "DingTalk";
    private const string WeComClientName = "WeCom";

    public static IServiceCollection AddHireBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddPersistence(services, configuration);
        services.AddMemoryCache();
        AddHttpClients(services, configuration);
        AddProviders(services, configuration);
        AddDomainServices(services);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        // ── Multi-tenancy Services ────────────────────────────────────────────────────
        services.AddScoped<ITenantContextProvider, TenantContextProvider>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
        }

        var cs = connectionString.Trim();

        // 连接串包含 Host= 或 postgresql 关键字时使用 PostgreSQL，否则回退到 SQLite（本地开发）
        if (cs.StartsWith("Host=", StringComparison.OrdinalIgnoreCase)
            || cs.Contains("postgresql", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<HireBotDbContext>((serviceProvider, options) =>
            {
                options.UseNpgsql(cs, npgsql => npgsql.MigrationsAssembly("HireBot.Repository"))
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
                
                // 添加多租户保存拦截器
                var logger = serviceProvider.GetRequiredService<ILogger<TenantSavingInterceptor>>();
                options.AddInterceptors(new TenantSavingInterceptor(serviceProvider, logger));
            });
        }
        else
        {
            services.AddDbContext<HireBotDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(cs)
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
                
                // 添加多租户保存拦截器
                var logger = serviceProvider.GetRequiredService<ILogger<TenantSavingInterceptor>>();
                options.AddInterceptors(new TenantSavingInterceptor(serviceProvider, logger));
            });
        }

        services.AddScoped<IHireBotRepository, HireBotRepository>();
    }

    private static void AddHttpClients(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(KingCrabClientName, (_, client) =>
        {
            ConfigureHttpClient(
                client,
                configuration["KingCrab:BaseUrl"] ?? configuration["KingCrew:BaseUrl"],
                configuration.GetValue<int?>("KingCrab:HttpTimeoutSeconds") ?? configuration.GetValue("KingCrew:HttpTimeoutSeconds", 120),
                "KingCrab:BaseUrl",
                "KingCrab:HttpTimeoutSeconds");
        });

        services.AddHttpClient(BuildServiceClientName, (_, client) =>
        {
            ConfigureHttpClient(
                client,
                configuration["BuildService:BaseUrl"],
                configuration.GetValue("BuildService:HttpTimeoutSeconds", 60),
                "BuildService:BaseUrl",
                "BuildService:HttpTimeoutSeconds");
        });

        services.AddHttpClient(FeishuClientName, (_, client) =>
        {
            ConfigureHttpClient(
                client,
                configuration["Feishu:BaseUrl"] ?? "https://open.feishu.cn",
                configuration.GetValue("Feishu:HttpTimeoutSeconds", 60),
                "Feishu:BaseUrl",
                "Feishu:HttpTimeoutSeconds");
        });

        services.AddHttpClient(DingTalkClientName, (_, client) =>
        {
            ConfigureHttpClient(
                client,
                configuration["DingTalk:BaseUrl"] ?? "https://oapi.dingtalk.com",
                configuration.GetValue("DingTalk:HttpTimeoutSeconds", 60),
                "DingTalk:BaseUrl",
                "DingTalk:HttpTimeoutSeconds");
        });

        services.AddHttpClient(WeComClientName, (_, client) =>
        {
            ConfigureHttpClient(
                client,
                configuration["WeCom:BaseUrl"] ?? "https://qyapi.weixin.qq.com",
                configuration.GetValue("WeCom:HttpTimeoutSeconds", 60),
                "WeCom:BaseUrl",
                "WeCom:HttpTimeoutSeconds");
        });
    }

    private static void AddProviders(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataProtection();

        services.AddScoped<IHiringRuntimeStore, PersistentHiringRuntimeStore>();
        services.AddSingleton<FileSystemSystemSkillRegistry>();
        services.AddSingleton<ISystemSkillRegistry>(sp => sp.GetRequiredService<FileSystemSystemSkillRegistry>());
        services.AddSingleton<ISkillCatalogProvider>(sp => sp.GetRequiredService<FileSystemSystemSkillRegistry>());
        services.AddSingleton<FileSystemTemplatePackageProvider>();
        services.AddSingleton<ITemplateDataProvider, BuildServiceTemplateDataProvider>();
        services.AddSingleton<ITemplatePackageProvider, BuildServiceTemplatePackageProvider>();
        services.AddSingleton<IDiscoveryRoleTemplatePackageProvider, FileSystemDiscoveryRoleTemplatePackageProvider>();
        services.AddSingleton<IWorkingTemplatePackageProvider, FileSystemWorkingTemplatePackageProvider>();
        services.AddSingleton<IDiscoveryRuleProvider, FileSystemDiscoveryRuleProvider>();
        services.AddSingleton<HiringStageCompletionEvaluator>();
        services.AddSingleton<IArtifactSerializer, PlaceholderArtifactSerializer>();
        services.AddSingleton<IHiringFileStore, FileSystemHiringFileStore>();
        services.AddSingleton<IEvaluationAssetStore, EvaluationAssetStore>();
        services.AddSingleton<SandboxPvcService>();
        services.AddSingleton<OpenSandboxProvisioner>();
        services.AddSingleton<KingCrabSandboxTokenProvider>();
        services.AddScoped<IKingCrabHttpClient, KingCrabHttpClient>();
        services.AddScoped<KingCrabGatewayClient>();
    }

    private static void AddDomainServices(IServiceCollection services)
    {
        // ── Identity & Context Services ────────────────────────────────────────────
        services.AddScoped<HireBot.Abstraction.Infrastructure.Identity.IUserIdentity, HireBot.Core.Infrastructure.Identity.HireBotUserIdentity>();

        // ── Domain Services ────────────────────────────────────────────────────────
        services.AddScoped<IEmployeeTemplateService, EmployeeTemplateService>();
        services.AddScoped<ITemplateSkillRecommendationService, TemplateSkillRecommendationService>();
        services.AddScoped<IEmployeeHiringService, EmployeeHiringService>();
        services.AddScoped<IStoreSkillPackageDownloader, StoreSkillPackageDownloader>();
        services.AddScoped<IInstanceArtifactCloneService, InstanceArtifactCloneService>();
        services.AddScoped<IInstanceArtifactResolver, InstanceArtifactResolver>();
      
        services.AddScoped<IInstanceRuntimeConversationService, InstanceRuntimeConversationService>();
        services.AddScoped<IHiringArtifactPackageService, HiringArtifactPackageService>();
        services.AddScoped<IHiringTodoService, HiringTodoService>();
        services.AddScoped<IEmployeeRuntimeService, EmployeeRuntimeService>();
        services.AddScoped<IInstanceChatService, InstanceChatService>();
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();

        services.AddScoped<IEvaluationService, EvaluationService>();
        services.AddScoped<ISandboxService, SandboxService>();
    }

    private static void ConfigureHttpClient(
        HttpClient client,
        string? baseUrl,
        int timeoutSeconds,
        string baseUrlKey,
        string timeoutKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{baseUrlKey} must be configured with an absolute URL.");
        }

        if (timeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"{timeoutKey} must be greater than zero.");
        }

        client.BaseAddress = uri;
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }
}

