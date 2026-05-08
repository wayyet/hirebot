namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal interface ITemplatePackageProvider
{
    Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default);
}
