using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HireBot.ApiService.Authentication;

internal static class DevelopmentAuthenticationDefaults
{
    public const string SchemeName = "Development";
    public const string SubjectHeader = "X-HireBot-Subject";
    public const string TenantHeader = "X-HireBot-Tenant";
    public const string OperatorHeader = "X-HireBot-Operator";
    public const string UsernameHeader = "X-HireBot-Username";
    public const string DisplayNameHeader = "X-HireBot-DisplayName";
    public const string DefaultSubject = "local-dev";
    public const string DefaultTenantId = "tenant-default";
    public const string DefaultOperatorId = "operator-default";
    public const string DefaultUsername = "local-dev";
    public const string DefaultDisplayName = "Local Developer";
}

internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = ResolveHeaderValue(DevelopmentAuthenticationDefaults.SubjectHeader, DevelopmentAuthenticationDefaults.DefaultSubject);
        var tenantId = ResolveHeaderValue(DevelopmentAuthenticationDefaults.TenantHeader, DevelopmentAuthenticationDefaults.DefaultTenantId);
        var operatorId = ResolveHeaderValue(DevelopmentAuthenticationDefaults.OperatorHeader, DevelopmentAuthenticationDefaults.DefaultOperatorId);
        var username = ResolveHeaderValue(DevelopmentAuthenticationDefaults.UsernameHeader, DevelopmentAuthenticationDefaults.DefaultUsername);
        var displayName = ResolveHeaderValue(DevelopmentAuthenticationDefaults.DisplayNameHeader, DevelopmentAuthenticationDefaults.DefaultDisplayName);

        var claims = new List<Claim>
        {
            new("sub", subject),
            new(ClaimTypes.NameIdentifier, subject),
            new("tenant_id", tenantId),
            new("operator_id", operatorId),
            new("preferred_username", username),
            new(ClaimTypes.Name, displayName),
            new("name", displayName),
            new(ClaimTypes.Email, $"{username}@local.dev")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string ResolveHeaderValue(string headerName, string defaultValue)
    {
        var value = Request.Headers[headerName].ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }
}
