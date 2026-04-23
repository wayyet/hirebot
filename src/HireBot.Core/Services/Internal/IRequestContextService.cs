namespace HireBot.Core.Services.Internal;

public interface IRequestContextService
{
    string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null);
    (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId);
}
