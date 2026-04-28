using HireBot.Core.Extensions;
using HireBot.Repository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorization();

var oidcAuthority = builder.Configuration["Security:OidcAuthority"];
if (!string.IsNullOrWhiteSpace(oidcAuthority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = oidcAuthority;
            options.Audience = builder.Configuration["Security:OidcAudience"];
            options.RequireHttpsMetadata = builder.Configuration.GetValue("Security:OidcRequireHttpsMetadata", true);
        });
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

using (var scope = app.Services.CreateScope())
{
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

if (!string.IsNullOrWhiteSpace(oidcAuthority))
{
    app.UseAuthentication();
}

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
