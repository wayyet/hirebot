using HireBot.Abstraction.Services.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Security;

public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector protector;
    private readonly ILogger<DataProtectionSecretProtector> logger;

    public DataProtectionSecretProtector(IDataProtectionProvider provider, IConfiguration configuration, ILogger<DataProtectionSecretProtector> logger)
    {
        var purpose = configuration["Security:SecretProtectionPurpose"];
        protector = provider.CreateProtector(string.IsNullOrWhiteSpace(purpose) ? "HireBot.IM_CONFIG.v1" : purpose.Trim());
        this.logger = logger;
    }

    public string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var protectedValue = protector.Protect(value.Trim());
            logger.LogDebug("加密成功: 原值长度={OriginalLength}, 加密后长度={ProtectedLength}", value.Length, protectedValue.Length);
            return protectedValue;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加密失败");
            return null;
        }
    }

    public string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var unprotectedValue = protector.Unprotect(value);
            logger.LogDebug("解密成功: 加密长度={ProtectedLength}, 解密后长度={OriginalLength}", value.Length, unprotectedValue.Length);
            return unprotectedValue;
        }
        catch (Exception ex)
        {
            var isEncryptedFormat = value.Contains('-') && value.Length > 50;
            if (isEncryptedFormat)
            {
                logger.LogError(ex, "解密失败：数据库中的值是加密格式，但无法用当前密钥解密。请重新保存 IM 配置。加密值长度={Length}", value.Length);
                return null;
            }
            logger.LogWarning(ex, "解密失败，返回原始值: {Value}", value);
            return value;
        }
    }
}
