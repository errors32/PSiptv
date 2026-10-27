using System.Globalization;
using System.Text.Json;

namespace PSiptv.Core;

public sealed record PlaylistExpiration(DateTimeOffset? ExpiresAt = null, bool IsUnlimited = false)
{
    public static PlaylistExpiration Unknown { get; } = new();

    public static PlaylistExpiration ParseXtream(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("user_info", out var user) ||
            user.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A fonte não devolveu a validade da lista.");
        var authenticated = user.TryGetProperty("auth", out var auth) && auth.ToString() == "1";
        DateTimeOffset? expiresAt = null;
        var hasDate = user.TryGetProperty("exp_date", out var date);
        if (hasDate && date.ValueKind is JsonValueKind.String or JsonValueKind.Number &&
            long.TryParse(date.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { }
        }
        // Some providers return auth=0 for an expired subscription, but still
        // supply its expiry date. Do not mistake an authentication error for unlimited validity.
        if (!authenticated && !(expiresAt.HasValue && user.TryGetProperty("status", out var status) &&
            status.ToString().Equals("Expired", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A fonte não permitiu consultar a validade da lista.");
        return new(expiresAt, authenticated && hasDate && date.ValueKind == JsonValueKind.Null);
    }

    public static PlaylistAccount? MetadataAccount(PlaylistAccount account)
    {
        if (account.Provider == ProviderType.Xtream) return account;
        if (account.Provider != ProviderType.M3U || !Uri.TryCreate(account.Url, UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https") || !url.AbsolutePath.EndsWith("/get.php", StringComparison.OrdinalIgnoreCase))
            return null;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (!query.TryAdd(key, value)) return null;
        }
        if (!query.TryGetValue("username", out var username) || string.IsNullOrWhiteSpace(username) ||
            !query.TryGetValue("password", out var password) || string.IsNullOrEmpty(password)) return null;
        var endpoint = new Uri(url, "player_api.php");
        return account with { Provider = ProviderType.Xtream,
            Url = endpoint.GetLeftPart(UriPartial.Path)[..^"/player_api.php".Length], Username = username, Password = password };
    }
}
