using System.Security.Cryptography;
using System.Text;

namespace PSiptv.Core;

public static class AccountDataProtection
{
    public const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string Prefix = "enc:v1:";

    public static byte[] CreateKey() => RandomNumberGenerator.GetBytes(KeySize);

    public static byte[] ProtectData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, string context)
    {
        RequireKey(key);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, data, ciphertext, tag, Encoding.UTF8.GetBytes(context));

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        ciphertext.CopyTo(payload, NonceSize + TagSize);
        return payload;
    }

    public static byte[] UnprotectData(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> key, string context)
    {
        RequireKey(key);
        if (payload.Length < NonceSize + TagSize)
            throw new InvalidOperationException("Os dados protegidos têm um formato inválido.");

        try
        {
            var plaintext = new byte[payload.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(payload[..NonceSize], payload.Slice(NonceSize + TagSize),
                payload.Slice(NonceSize, TagSize), plaintext, Encoding.UTF8.GetBytes(context));
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Não foi possível desencriptar os dados protegidos.", ex);
        }
    }

    public static PlaylistAccount Protect(PlaylistAccount account, ReadOnlySpan<byte> key) => account with
    {
        Url = Protect(account.Url, key, account.Id, nameof(account.Url)),
        Username = Protect(account.Username, key, account.Id, nameof(account.Username)),
        Password = Protect(account.Password, key, account.Id, nameof(account.Password)),
        EpgUrl = Protect(account.EpgUrl, key, account.Id, nameof(account.EpgUrl))
    };

    public static PlaylistAccount Unprotect(PlaylistAccount account, ReadOnlySpan<byte> key) => account with
    {
        Url = Unprotect(account.Url, key, account.Id, nameof(account.Url)),
        Username = Unprotect(account.Username, key, account.Id, nameof(account.Username)),
        Password = Unprotect(account.Password, key, account.Id, nameof(account.Password)),
        EpgUrl = Unprotect(account.EpgUrl, key, account.Id, nameof(account.EpgUrl))
    };

    private static string Protect(string value, ReadOnlySpan<byte> key, string accountId, string field)
    {
        RequireKey(key);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(accountId, field));

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        ciphertext.CopyTo(payload, NonceSize + TagSize);
        CryptographicOperations.ZeroMemory(plaintext);
        return Prefix + Convert.ToBase64String(payload);
    }

    private static string Unprotect(string value, ReadOnlySpan<byte> key, string accountId, string field)
    {
        RequireKey(key);
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Os dados protegidos da lista têm um formato inválido.");

        try
        {
            var payload = Convert.FromBase64String(value[Prefix.Length..]);
            if (payload.Length < NonceSize + TagSize)
                throw new CryptographicException();

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var ciphertext = payload.AsSpan(NonceSize + TagSize);
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, TagSize))
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(accountId, field));

            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException("Não foi possível desencriptar os dados da lista.", ex);
        }
    }

    private static byte[] AssociatedData(string accountId, string field) =>
        Encoding.UTF8.GetBytes($"PSiptv\0{accountId}\0{field}");

    private static void RequireKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"A chave deve ter {KeySize} bytes.", nameof(key));
    }
}
