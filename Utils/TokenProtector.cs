using System;
using System.Security.Cryptography;
using System.Text;
namespace MuSync.Utils;

/// <summary>
/// 用 Windows DPAPI（当前用户范围）保护敏感配置字段（Steam refresh token 等）。
/// 输出带 "DPAPI:" 前缀；无法解密（换用户/换机器）时返回空，旧版明文格式原样返回以兼容迁移。
/// </summary>
internal static class TokenProtector
{
    private const string Prefix = "DPAPI:";

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        catch
        {
            // 保护失败退回明文，保证可用性（不阻断登录）
            return plain;
        }
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // 旧版本明文配置或未知格式：原样返回
            return stored;
        }
        try
        {
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(stored[Prefix.Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // 无法解密（例如换了 Windows 用户/系统重装）：视为无效令牌
            return "";
        }
    }
}
