using System.Security.Cryptography;

namespace PSiptv.Core;

public static class PinProtection
{
    private const int Iterations = 210_000;
    public static bool IsValid(string pin) => pin.Length is >= 4 and <= 8 && pin.All(c => c is >= '0' and <= '9');

    public static string Hash(string pin)
    {
        if (!IsValid(pin)) throw new InvalidOperationException("O PIN deve ter entre 4 e 8 algarismos.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string stored)
    {
        if (!IsValid(pin)) return false;
        try
        {
            var parts = stored.Split('.');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(hash, expected);
        }
        catch (FormatException) { return false; }
    }
}
