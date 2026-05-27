using Microsoft.AspNetCore.DataProtection;

namespace Automated_FTP.Infrastructure.Security;

/// <summary>
/// 用 ASP.NET Core DataProtection 对 FTP 密码进行加解密。
/// 入库前调用 Protect 转密文，运行时调用 Unprotect 拿明文。
/// 兼容入库期间只是明文（未加密）的情况：Unprotect 失败时退回原值，便于灰度迁移。
/// </summary>
public class PasswordProtector
{
    private const string Purpose = "FTP_UPLOAD_CONFIG.FTP_PASSWORD";
    private const string EncryptedPrefix = "enc:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<PasswordProtector> _logger;

    public PasswordProtector(IDataProtectionProvider provider, ILogger<PasswordProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        return EncryptedPrefix + _protector.Protect(plain);
    }

    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;

        if (!stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            _logger.LogWarning("FTP 密码字段似乎是明文存储，建议尽快用 /api/diagnostics/protect-password 加密后回写。");
            return stored;
        }

        var payload = stored[EncryptedPrefix.Length..];
        try
        {
            return _protector.Unprotect(payload);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("FTP 密码解密失败：DataProtection 密钥可能与加密时不一致。", ex);
        }
    }
}
