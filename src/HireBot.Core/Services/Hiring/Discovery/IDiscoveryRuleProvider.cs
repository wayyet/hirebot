namespace HireBot.Core.Services.Hiring.Discovery;

internal interface IDiscoveryRuleProvider
{
    Task<DiscoverySkillDefinition> LoadAsync(CancellationToken cancellationToken = default);
}
