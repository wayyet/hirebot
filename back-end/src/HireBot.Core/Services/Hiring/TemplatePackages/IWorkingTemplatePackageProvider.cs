namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal interface IWorkingTemplatePackageProvider
{
    Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default);
}
