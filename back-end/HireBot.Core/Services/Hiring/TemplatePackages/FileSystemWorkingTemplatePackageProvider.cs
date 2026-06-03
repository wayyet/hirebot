using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal sealed class FileSystemWorkingTemplatePackageProvider(
    FileSystemTemplatePackageProvider templatePackageProvider,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : IWorkingTemplatePackageProvider
{
    private const string DefaultRelativePath = "Assets/TemplatePackages/hiring-working-skeleton";

    public Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        var resolvedPath = HiringAssetFileSystem.ResolveDirectory(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:WorkingTemplateSkeletonPath"],
            DefaultRelativePath);
        return templatePackageProvider.LoadFromDirectoryAsync(
            resolvedPath,
            "hiring-working-skeleton",
            cancellationToken);
    }
}
