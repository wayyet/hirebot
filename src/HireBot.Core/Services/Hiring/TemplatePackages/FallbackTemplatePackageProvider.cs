using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal sealed class FallbackTemplatePackageProvider(
    BuildServiceTemplatePackageProvider buildServiceProvider,
    FileSystemTemplatePackageProvider fileSystemProvider,
    ILogger<FallbackTemplatePackageProvider> logger) : ITemplatePackageProvider
{
    public async Task<TemplatePackageDefinition> LoadAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await buildServiceProvider.LoadAsync(templateId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Template package download unavailable. Falling back to built-in NCrewTemplate. TemplateId={TemplateId}",
                templateId);
        }

        return await fileSystemProvider.LoadAsync(templateId, cancellationToken);
    }
}
