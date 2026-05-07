﻿using HireBot.Abstraction;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeTemplate;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Abstraction.Services.Team;
using HireBot.Abstraction.Services.Training;
using HireBot.Abstraction.Services.User;
using HireBot.Core.Providers;
using HireBot.Core.Services;
using HireBot.Core.Services.Collaboration;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.EmployeeTemplate;
using HireBot.Core.Services.Evaluation;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Security;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Core.Services.Team;
using HireBot.Core.Services.Training;
using HireBot.Abstraction.Services.Security;
using HireBot.Repository;
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
        AddHttpClients(services, configuration);
        AddProviders(services, configuration);
        AddDomainServices(services);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
        }

        services.AddDbContext<HireBotDbContext>(options => options
            .UseNpgsql(
                connectionString.Trim(),
                npgsql => npgsql.MigrationsAssembly("HireBot.Repository"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

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
        services.AddScoped<IRequestContextService, RequestContextService>();
        services.AddDataProtection();

        services.AddSingleton<IEmployeeRuntimeStore, InMemoryEmployeeRuntimeStore>();
        services.AddScoped<IHiringRuntimeStore, PersistentHiringRuntimeStore>();
        services.AddSingleton<IEvaluationScenarioProvider, UnavailableEvaluationScenarioProvider>();
        services.AddSingleton<ICollaborationProvider, UnavailableCollaborationProvider>();
        services.AddSingleton<ITeamImProvider, InMemoryTeamImProvider>();
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
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IEmployeeTemplateService, EmployeeTemplateService>();
        services.AddScoped<IEmployeeHiringService, EmployeeHiringService>();
        services.AddScoped<IInstanceArtifactCloneService, InstanceArtifactCloneService>();
        services.AddScoped<IInstanceArtifactResolver, InstanceArtifactResolver>();
      
        services.AddScoped<IInstanceRuntimeConversationService, InstanceRuntimeConversationService>();
        services.AddScoped<IHiringArtifactPackageService, HiringArtifactPackageService>();
        services.AddScoped<IEmployeeRuntimeService, EmployeeRuntimeService>();
        services.AddScoped<IInstanceChatService, InstanceChatService>();
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IInstanceImConfigService, InstanceImConfigService>();
      
    

        services.AddScoped<ITrainingService, TrainingService>();
        services.AddScoped<IEvaluationService, EvaluationService>();
        services.AddScoped<ISandboxService, SandboxService>();
        services.AddScoped<ICollaborationService, CollaborationService>();
        services.AddScoped<ITeamImService, TeamImService>();
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

