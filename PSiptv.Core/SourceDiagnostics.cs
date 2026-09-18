namespace PSiptv.Core;

public enum SourceDiagnosticState { Passed, Warning, Failed, Skipped }

public sealed record SourceDiagnosticCheck(
    string Name,
    SourceDiagnosticState State,
    string Detail,
    long ElapsedMilliseconds = 0);

public sealed record SourceDiagnosticReport(
    string SourceName,
    string ProviderName,
    string Location,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SourceDiagnosticCheck> Checks)
{
    public SourceDiagnosticState State => Checks.Any(item => item.State == SourceDiagnosticState.Failed)
        ? SourceDiagnosticState.Failed
        : Checks.Any(item => item.State == SourceDiagnosticState.Warning)
            ? SourceDiagnosticState.Warning
            : SourceDiagnosticState.Passed;
}

public static class SourceDiagnosticPolicy
{
    public static string PublicLocation(PlaylistAccount account)
    {
        if (account.Provider == ProviderType.LocalM3U)
            return Path.GetFileName(account.Url);
        if (!Uri.TryCreate(account.Url, UriKind.Absolute, out var uri)) return "Endereço inválido";
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    public static string SafeError(Exception exception, PlaylistAccount account)
    {
        var message = exception switch
        {
            OperationCanceledException => "O teste excedeu o tempo limite.",
            HttpRequestException { StatusCode: { } status } => $"O servidor respondeu HTTP {(int)status} ({status}).",
            HttpRequestException => "Não foi possível estabelecer ligação ao servidor.",
            _ => exception.Message.Trim()
        };
        foreach (var secret in new[] { account.Username, account.Password }
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            message = message.Replace(secret, "•••", StringComparison.Ordinal)
                .Replace(Uri.EscapeDataString(secret), "•••", StringComparison.OrdinalIgnoreCase);
        }
        return string.IsNullOrWhiteSpace(message) ? "O teste falhou sem detalhes adicionais." : message;
    }
}
