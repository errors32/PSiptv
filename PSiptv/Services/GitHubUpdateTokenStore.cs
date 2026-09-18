namespace PSiptv.Services;

public static class GitHubUpdateTokenStore
{
    private const string TokenKey = "github-releases.token";

    public static async Task<string> ReadAsync() =>
        (await SecureStorage.Default.GetAsync(TokenKey))?.Trim() ?? "";

    public static async Task SaveAsync(string token)
    {
        var normalized = Normalize(token);
        await SecureStorage.Default.SetAsync(TokenKey, normalized);
    }

    public static void Remove() => SecureStorage.Default.Remove(TokenKey);

    private static string Normalize(string token)
    {
        var normalized = token.Trim();
        if (normalized.Length == 0) throw new InvalidOperationException("Introduza um token GitHub.");
        if (normalized.Any(char.IsWhiteSpace) || normalized.IndexOfAny(['\\', '"', '\'']) >= 0)
            throw new InvalidOperationException(
                "Cole apenas o token GitHub, sem aspas, barras invertidas ou espaços.");
        return normalized;
    }
}
