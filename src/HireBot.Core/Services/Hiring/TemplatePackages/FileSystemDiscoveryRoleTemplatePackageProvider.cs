using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal sealed class FileSystemDiscoveryRoleTemplatePackageProvider(
    FileSystemTemplatePackageProvider templatePackageProvider,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : IDiscoveryRoleTemplatePackageProvider
{
    private const string DefaultRelativePath = "Assets/SystemSkills/digital-employee-discovery";

    public Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        var resolvedPath = HiringAssetFileSystem.ResolveDirectory(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DiscoveryRoleTemplatePath"],
            DefaultRelativePath);
        return templatePackageProvider.LoadFromDirectoryAsync(
            resolvedPath,
            "digital-employee-discovery",
            cancellationToken);
    }
}
