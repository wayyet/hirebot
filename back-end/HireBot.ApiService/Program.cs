using HireBot.ApiService.Authentication;
using HireBot.ApiService.McpTools;
using HireBot.ApiService.Serialization;
using HireBot.Core.Extensions;
using HireBot.Core.Services.Internal;
using ModelContextProtocol.Protocol;
using HireBot.Repository;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Serilog;
using Serilog.Events;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var logPath = builder.Configuration["Serilog:LogPath"] ?? "logs/hirebot-.log";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore.Server.Kestrel", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Async(wt => wt.File(
        logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .CreateLogger();

builder.Host.UseSerilog();

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new DateTimeOffsetMinuteConverter());
    });
builder.Services.AddDirectoryBrowser();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddAuthorization();

var oidcAuthority = builder.Configuration["Security:OidcAuthority"];
var authenticationScheme = string.IsNullOrWhiteSpace(oidcAuthority)
    ? DevelopmentAuthenticationDefaults.SchemeName
    : JwtBearerDefaults.AuthenticationScheme;

var authenticationBuilder = builder.Services.AddAuthentication(authenticationScheme);
if (!string.IsNullOrWhiteSpace(oidcAuthority))
{
    var validOidcValues = BuildOidcValidationValues(
        builder.Configuration["Security:OidcAudience"],
        builder.Configuration["Security:OidcClientId"]);

    if (validOidcValues.Count == 0)
    {
        throw new InvalidOperationException(
            "启用 OIDC 鉴权时，必须至少配置 Security:OidcAudience 或 Security:OidcClientId。");
    }

    authenticationBuilder.AddJwtBearer(options =>
    {
        options.Authority = oidcAuthority;
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Security:OidcRequireHttpsMetadata", true);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            AudienceValidator = (tokenAudiences, securityToken, _) =>
                IsValidOidcToken(tokenAudiences, securityToken, validOidcValues),
        };
    });
}
else
{
    authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationDefaults.SchemeName,
        _ => { });
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", cors =>
    {
        cors.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddHireBotServices(builder.Configuration);

// MCP Server：供 Kingcrab 等 Agent 调用的工具端点
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new Implementation { Name = "HireBot MCP Server", Version = "1.0.0" };
}).WithHttpTransport(options => { options.Stateless = true; })
  .WithTools<HiringTodoMcpTools>();

var app = builder.Build();

if (builder.Configuration.GetValue("Database:AutoMigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HireBotDbContext>();
    // SQLite 使用 EnsureCreated 直接从模型建表（无需运行 PostgreSQL 迁移脚本）
    if (dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        await dbContext.Database.EnsureCreatedAsync();
    else
        await dbContext.Database.MigrateAsync();


}

var evaluationResourceRoot = ResolveEvaluationResourceRoot(
    app.Environment.ContentRootPath,
    builder.Configuration["HireBot:DataRoot"],
    builder.Configuration["HireBot:EvaluationResourceRoot"]);
Directory.CreateDirectory(evaluationResourceRoot);

app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(evaluationResourceRoot),
    RequestPath = "/resources"
});
app.UseDirectoryBrowser(new DirectoryBrowserOptions
{
    FileProvider = new PhysicalFileProvider(evaluationResourceRoot),
    RequestPath = "/resources"
});
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/resources", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});
// 提供默认文件（index.html），使 ASP.NET Core 可作为前端宿主
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

// /resources/evaluation 诊断：直接返回磁盘路径和文件列表（验证路径解析是否正确）
app.MapGet("/api/diagnostics/evaluation-root", () =>
{
    var root = evaluationResourceRoot;
    var evalDir = Path.Combine(root, "evaluation");
    var exists = Directory.Exists(evalDir);
    var dirs = exists ? Directory.GetDirectories(evalDir).Select(Path.GetFileName).ToArray() : Array.Empty<string>();
    return Results.Ok(new
    {
        evaluationResourceRoot = root,
        evaluationDir = evalDir,
        evaluationDirExists = exists,
        sessionCount = dirs.Length,
        sessions = dirs,
        contentRoot = app.Environment.ContentRootPath
    });
});

app.UseAuthentication();
app.UseMiddleware<UserSyncMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapMcp("/mcp");

// 向前端 SPA 注入运行时配置（OIDC / API 地址），支持镜像内前后端合并部署
app.MapGet("/runtime-config.js", (IConfiguration cfg) =>
{
    var consoleCfg = cfg.GetSection("ConsoleAuth");
    // 使用 JsonSerializer 对字符串值做安全转义，防止注入
    var config = new
    {
        Authority = consoleCfg["Authority"] ?? string.Empty,
        Realm = consoleCfg["Realm"] ?? string.Empty,
        ClientId = consoleCfg["ClientId"] ?? string.Empty,
        BypassAuth = consoleCfg.GetValue("BypassAuth", false),
        EnableWarmTheme = ResolveEnableWarmTheme(cfg),
        ApiBase = string.Empty,
        TemplateApiBase = consoleCfg["TemplateApiBase"] ?? string.Empty,
        MaxActivePersonalClonesPerOwner = ResolveMaxActivePersonalClonesPerOwner(cfg),
    };
    var json = System.Text.Json.JsonSerializer.Serialize(config);
    return Results.Content($"window.__AUTH_CONFIG__ = {json};", "application/javascript");
}).ExcludeFromDescription();

// 防止未匹配的 /api/* 路由被 SPA 回退捕获（应返回 404）
app.Map("/api/{**path}", () => Results.NotFound()).ExcludeFromDescription();

// MCP 端点不属于 SPA 路由，GET 请求返回 405（正确语义），POST 请求由 MapMcp 处理
app.MapGet("/mcp", () => Results.StatusCode(405)).ExcludeFromDescription();

// SPA 回退：前端路由（如 /jobs/123）由 index.html 接管
app.MapFallbackToFile("index.html");

await app.RunAsync();

static string ResolveEvaluationResourceRoot(string contentRootPath, string? configuredDataRoot, string? configuredResourceRoot)
{
    return HireBotPathResolver.ResolveEvaluationResourceRoot(
        contentRootPath,
        configuredDataRoot,
        configuredResourceRoot);
}

static int ResolveMaxActivePersonalClonesPerOwner(IConfiguration configuration)
{
    const int defaultLimit = 10;
    var configured = configuration["HireBot:MaxActivePersonalClonesPerOwner"];
    return int.TryParse(configured, out var value) && value > 0
        ? value
        : defaultLimit;
}

static bool ResolveEnableWarmTheme(IConfiguration configuration)
{
    return configuration.GetValue("ConsoleUi:EnableWarmTheme", true);
}

static IReadOnlyCollection<string> BuildOidcValidationValues(params string?[] values)
{
    return values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .SelectMany(value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static bool IsValidOidcToken(
    IEnumerable<string>? tokenAudiences,
    SecurityToken securityToken,
    IReadOnlyCollection<string> validOidcValues)
{
    var hasMatchingAudience = tokenAudiences?.Any(validOidcValues.Contains) == true;
    if (hasMatchingAudience)
    {
        return true;
    }

    // .NET 10 的 JwtBearerHandler 默认将 token 解析为 JsonWebToken（Microsoft.IdentityModel.JsonWebTokens）
    // 而非旧版 JwtSecurityToken（System.IdentityModel.Tokens.Jwt），需兼容两种类型
    string? authorizedParty = securityToken switch
    {
        Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt => jwt.TryGetClaim("azp", out var azpClaim) ? azpClaim.Value : null,
        JwtSecurityToken legacyJwt => legacyJwt.Claims
            .FirstOrDefault(c => string.Equals(c.Type, "azp", StringComparison.Ordinal))?.Value,
        _ => null,
    };

    return !string.IsNullOrWhiteSpace(authorizedParty) && validOidcValues.Contains(authorizedParty);
}
