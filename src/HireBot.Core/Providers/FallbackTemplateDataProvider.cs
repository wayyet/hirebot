using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Providers;

internal sealed class FallbackTemplateDataProvider(
    BuildServiceTemplateDataProvider buildServiceProvider,
    FileSystemTemplateDataProvider fileSystemProvider,
    ILogger<FallbackTemplateDataProvider> logger) : ITemplateDataProvider
{
    public async Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var templates = await buildServiceProvider.GetAllAsync(cancellationToken);
            if (templates.Count > 0)
            {
                return templates;
            }

            logger.LogWarning("Build service returned no employee templates. Falling back to built-in template assets.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Build service employee templates unavailable. Falling back to built-in template assets.");
        }

        return await fileSystemProvider.GetAllAsync(cancellationToken);
    }

    public async Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await buildServiceProvider.GetByIdAsync(templateId, cancellationToken);
            if (template is not null)
            {
                return template;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Build service employee template detail unavailable. Falling back to built-in template assets. TemplateId={TemplateId}",
                templateId);
        }

        return await fileSystemProvider.GetByIdAsync(templateId, cancellationToken);
    }
}
