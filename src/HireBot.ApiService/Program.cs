using HireBot.Core.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using HireBot.ApiService.Swagger;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// 添加服务
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

// 配置 CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// 注册 HireBot 服务
builder.Services.AddHireBotServices(builder.Configuration);

var app = builder.Build();

// 配置中间件
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
if (!string.IsNullOrWhiteSpace(oidcAuthority))
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
