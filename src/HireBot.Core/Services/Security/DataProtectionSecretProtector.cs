using HireBot.Abstraction.Services.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace HireBot.Core.Services.Security;

public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider, IConfiguration configuration)
    {
        var purpose = configuration["Security:SecretProtectionPurpose"];
        protector = provider.CreateProtector(string.IsNullOrWhiteSpace(purpose) ? "HireBot.IM_CONFIG.v1" : purpose.Trim());
    }

    public string? Protect(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : protector.Protect(value.Trim());
    }

    public string? Unprotect(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : protector.Unprotect(value);
    }
}

