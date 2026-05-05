using HireBot.ApiService.Authentication;
using HireBot.Core.Extensions;
using HireBot.Repository;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using HireBot.ApiService.Swagger;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.MapType<IFormFile>(() => new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" });
    options.OperationFilter<FormFileOperationFilter>();
});
builder.Services.AddHttpContextAccessor();
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

var app = builder.Build();

if (builder.Configuration.GetValue("Database:AutoMigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HireBotDbContext>();
    await dbContext.Database.MigrateAsync();
}

var evaluationResourceRoot = ResolveEvaluationResourceRoot(
    app.Environment.ContentRootPath,
    builder.Configuration["HireBot:EvaluationResourceRoot"]);
Directory.CreateDirectory(evaluationResourceRoot);

app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(evaluationResourceRoot),
    RequestPath = "/resources"
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

static string ResolveEvaluationResourceRoot(string contentRootPath, string? configuredResourceRoot)
{
    if (string.IsNullOrWhiteSpace(configuredResourceRoot))
    {
        return Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot", "resources"));
    }

    return Path.IsPathRooted(configuredResourceRoot)
        ? Path.GetFullPath(configuredResourceRoot.Trim())
        : Path.GetFullPath(Path.Combine(contentRootPath, configuredResourceRoot.Trim()));
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

    if (securityToken is not JwtSecurityToken jwtSecurityToken)
    {
        return false;
    }

    var authorizedParty = jwtSecurityToken.Claims
        .FirstOrDefault(claim => string.Equals(claim.Type, "azp", StringComparison.Ordinal))
        ?.Value;

    return !string.IsNullOrWhiteSpace(authorizedParty) && validOidcValues.Contains(authorizedParty);
}
