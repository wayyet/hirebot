namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal interface IDiscoveryRoleTemplatePackageProvider
{
    Task<TemplatePackageDefinition> LoadAsync(CancellationToken cancellationToken = default);
}
