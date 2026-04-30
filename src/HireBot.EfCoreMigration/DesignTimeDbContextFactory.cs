using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HireBot.EfCoreMigration;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HireBotDbContext>
{
    public HireBotDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for design-time migrations.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<HireBotDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString.Trim(),
            npgsql => npgsql.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name));

        return new HireBotDbContext(optionsBuilder.Options);
    }
}

