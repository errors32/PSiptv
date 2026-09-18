using System.Security.Cryptography;
using System.Text;

namespace PSiptv.Core;

public static class GitHubTokenBackupProtection
{
    private const string Prefix = "enc:v1:";
    private const string Context = "backup:github-releases.token:v1";

    // This application key makes the protected value portable between devices.
    // SecureStorage remains responsible for protecting the restored token locally.
    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes(
        "PSiptv portable GitHub release token backup 2026-09"));

    public static string Protect(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "";
        var plaintext = Encoding.UTF8.GetBytes(token.Trim());
        try
        {
            return Prefix + Convert.ToBase64String(
                AccountDataProtection.ProtectData(plaintext, Key, Context));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string Unprotect(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)) return "";
        if (!protectedToken.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("O token GitHub protegido tem um formato inválido.");

        try
        {
            var payload = Convert.FromBase64String(protectedToken[Prefix.Length..]);
            var plaintext = AccountDataProtection.UnprotectData(payload, Key, Context);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("O token GitHub protegido tem um formato inválido.", ex);
        }
    }
}
