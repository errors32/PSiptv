using System.Security.Cryptography;
using System.Text;

namespace PSiptv.Core;

/// <summary>Portable, authenticated backup envelope with a password-derived key.</summary>
public static class BackupProtection
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PSIPTVBACKUP\0\x01");
    private const int SaltSize = 16;
    private const int Iterations = 600_000;
    private const string Context = "PSiptv password backup v1";
    public const int MaximumFileSize = 50_000_000;
    public static bool IsProtected(ReadOnlySpan<byte> data) => data.StartsWith(Magic);

    public static byte[] Protect(ReadOnlySpan<byte> plaintext, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Introduza uma palavra-passe.", nameof(password));
        if (plaintext.Length > MaximumFileSize - Magic.Length - SaltSize - 28)
            throw new InvalidOperationException("A cópia de segurança excede o limite de 50 MB.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            var payload = AccountDataProtection.ProtectData(plaintext, key, Context);
            return [.. Magic, .. salt, .. payload];
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static byte[] Unprotect(ReadOnlySpan<byte> data, string? password)
    {
        if (data.Length > MaximumFileSize) throw new InvalidOperationException("A cópia de segurança excede o limite de 50 MB.");
        if (!IsProtected(data)) return data.ToArray(); // Existing JSON backups remain readable.
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Esta cópia requer uma palavra-passe.");
        if (data.Length < Magic.Length + SaltSize + 28) throw new InvalidOperationException("A cópia de segurança está incompleta.");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, data.Slice(Magic.Length, SaltSize), Iterations, HashAlgorithmName.SHA256, 32);
        try { return AccountDataProtection.UnprotectData(data[(Magic.Length + SaltSize)..], key, Context); }
        catch (InvalidOperationException ex) { throw new InvalidOperationException("Palavra-passe incorreta ou cópia de segurança danificada.", ex); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
