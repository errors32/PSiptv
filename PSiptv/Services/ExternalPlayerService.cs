namespace PSiptv.Services;

public sealed record ExternalPlayerOption(string PlayerId, string DisplayName);

public static class ExternalPlayerService
{
    // Keep the original preference key so existing Android selections continue to work.
    private const string PlayerKey = "externalPlayerPackage";
    private const string NameKey = "externalPlayerName";
    public const string SystemPlayerId = "system";
    public const string VlcPlayerId = "vlc";

    public static string SelectedPlayerId => Preferences.Default.Get(PlayerKey, "");
    public static bool IsConfigured => SelectedPlayerId.Length > 0;
    public static string SelectedName => SelectedPlayerId switch
    {
        SystemPlayerId => LanguageService.Text(SystemPlayerName),
        VlcPlayerId => "VLC",
        _ => Preferences.Default.Get(NameKey, LanguageService.Text("Nenhum leitor selecionado"))
    };

    public static string ExperienceDescription =>
#if ANDROID
        "Escolha a aplicação que recebe diretamente o endereço do vídeo.";
#elif WINDOWS
        "O Windows apresenta o seletor de aplicações sempre que abrir um conteúdo externo.";
#elif IOS
        "Use o seletor do iOS ou abra diretamente no VLC, quando estiver instalado.";
#elif MACCATALYST
        "O macOS abre uma lista M3U na aplicação associada a este tipo de ficheiro.";
#else
        "O sistema abre uma lista M3U numa aplicação compatível.";
#endif

    private static string SystemPlayerName =>
#if WINDOWS
        "Seletor de aplicações do Windows";
#elif IOS
        "Seletor de aplicações do iOS";
#elif MACCATALYST
        "Aplicação predefinida do macOS";
#else
        "Aplicação predefinida do sistema";
#endif

    public static async Task<bool> ChooseAsync(Page owner)
    {
#if ANDROID
        var players = await MainThread.InvokeOnMainThreadAsync(GetInstalledPlayers);
        if (players.Count == 0)
        {
            await LanguageService.AlertAsync(owner, "Leitor externo", "Não foi encontrado nenhum leitor de vídeo compatível.");
            return false;
        }

        var labels = players.Select(player => player.DisplayName).ToArray();
        for (var index = 0; index < labels.Length; index++)
        {
            if (labels.Count(label => string.Equals(label, labels[index], StringComparison.OrdinalIgnoreCase)) > 1)
                labels[index] = $"{labels[index]} ({players[index].PlayerId})";
        }

        var selected = await LanguageService.ActionSheetAsync(owner, "Escolher leitor de vídeo", "Cancelar", null, labels);
        var selectedIndex = Array.IndexOf(labels, selected);
        if (selectedIndex < 0) return false;
        Save(players[selectedIndex].PlayerId, players[selectedIndex].DisplayName);
        return true;
#elif IOS
        var players = new List<ExternalPlayerOption>
        {
            new(SystemPlayerId, LanguageService.Text(SystemPlayerName))
        };
        if (await Launcher.Default.CanOpenAsync(new Uri("vlc-x-callback://x-callback-url/stream")))
            players.Add(new(VlcPlayerId, "VLC"));

        var labels = players.Select(player => player.DisplayName).ToArray();
        var selected = await LanguageService.ActionSheetAsync(owner, "Escolher leitor de vídeo", "Cancelar", null, labels);
        var selectedIndex = Array.IndexOf(labels, selected);
        if (selectedIndex < 0) return false;
        Save(players[selectedIndex].PlayerId, players[selectedIndex].DisplayName);
        return true;
#else
        Save(SystemPlayerId, LanguageService.Text(SystemPlayerName));
        await LanguageService.AlertAsync(owner, "Leitor externo", ExperienceDescription);
        return true;
#endif
    }

    private static void Save(string playerId, string displayName)
    {
        Preferences.Default.Set(PlayerKey, playerId);
        Preferences.Default.Set(NameKey, displayName);
    }

#if ANDROID
    private static List<ExternalPlayerOption> GetInstalledPlayers()
    {
        var activity = Platform.CurrentActivity;
        var packageManager = activity?.PackageManager;
        if (packageManager is null) return [];

        var resolved = new List<Android.Content.PM.ResolveInfo>();
        foreach (var mime in new[]
                 {
                     "video/*", "application/x-mpegURL", "application/vnd.apple.mpegurl", "audio/x-mpegurl"
                 })
        {
            using var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
            intent.SetDataAndType(Android.Net.Uri.Parse("https://www.example.com/video.m3u8"), mime);
#pragma warning disable CA1422 // Compatibility overload for Android versions below 13.
            resolved.AddRange(packageManager.QueryIntentActivities(intent, (Android.Content.PM.PackageInfoFlags)0));
#pragma warning restore CA1422
        }

        return resolved
            .Where(result => result.ActivityInfo?.PackageName is not null)
            .GroupBy(result => result.ActivityInfo!.PackageName!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var result = group.First();
                var package = result.ActivityInfo!.PackageName!;
                return new ExternalPlayerOption(package, result.LoadLabel(packageManager)?.ToString() ?? package);
            })
            .OrderBy(player => player.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
#endif
}
