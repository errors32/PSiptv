using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class PodcastSettingsPage : LocalizedPage
{
    private readonly Label syncStatus = Ui.Text("", 13, true);
    private readonly VerticalStackLayout feeds = Ui.Stack();
    public PodcastSettingsPage()
    {
        Ui.Page(this, "Definições dos podcasts");
        var account = AppServices.ActiveAccount;
        var profile = UserProfileService.Active.Id;
        var options = account is null ? new PodcastOptions() : PodcastAutomationService.Options(account.Id, profile);
        var automatic = new Switch { IsToggled = options.AutomaticDownloads };
        var wifi = new Switch { IsToggled = options.WifiOnly };
        var deletePlayed = new Switch { IsToggled = options.DeletePlayedDownloads };
        var sizes = new[] { 128, 256, 512, 1024, 2048, 5120, 10240 };
        var limit = new Picker { ItemsSource = sizes.Select(n => $"{n} MB").ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(sizes, options.MaximumMegabytes)) };
        limit.SetDynamicResource(Picker.TextColorProperty, "Ink");
        void Save()
        {
            if (AppServices.ActiveAccount?.Id != account?.Id || UserProfileService.Active.Id != profile) return;
            options = options with { AutomaticDownloads = automatic.IsToggled, WifiOnly = wifi.IsToggled,
                DeletePlayedDownloads = deletePlayed.IsToggled, MaximumMegabytes = sizes[Math.Max(0, limit.SelectedIndex)] };
            PodcastAutomationService.Save(options);
        }
        automatic.Toggled += (_, _) => Save();
        wifi.Toggled += (_, _) => Save();
        deletePlayed.Toggled += (_, _) => Save();
        limit.SelectedIndexChanged += (_, _) => Save();
        foreach (var feed in FavoritesService.Items.Where(e => e.HasEpisodes && PodcastFeed.IsPodcast(e)))
        {
            var selected = new Switch { IsToggled = options.SelectedFeeds is null || options.SelectedFeeds.Contains(feed.Id) };
            selected.Toggled += (_, e) =>
            {
                var ids = (options.SelectedFeeds ?? FavoritesService.Items.Where(p => p.HasEpisodes && PodcastFeed.IsPodcast(p))
                    .Select(p => p.Id).ToArray()).ToHashSet();
                if (e.Value) ids.Add(feed.Id); else ids.Remove(feed.Id);
                options = options with { SelectedFeeds = ids.ToArray() };
                Save();
            };
            feeds.Add(Ui.Stack(Ui.Text(feed.Name), selected));
        }
        RefreshSyncStatus();
        var sync = Ui.Button("Sincronizar agora", async () =>
        {
            await FavoritesSyncService.SynchronizeAsync();
            RefreshSyncStatus();
        });
        var body = Ui.Stack(
            Ui.Card(Ui.Stack(Ui.Text("Downloads automáticos", 20),
                Ui.Text("Descarregar automaticamente"), automatic,
                Ui.Text("Desligado por defeito. Verifica novos episódios enquanto a aplicação está aberta. Na primeira ativação descarrega apenas o episódio mais recente de cada podcast escolhido.", 13, true),
                Ui.Text("Apenas por Wi-Fi"), wifi, Ui.Text("Limite de espaço para podcasts"), limit,
                Ui.Text("Apagar downloads dos episódios ouvidos"), deletePlayed,
                Ui.Text("Podcasts escolhidos", 18), feeds)),
            Ui.Card(Ui.Stack(Ui.Text("Sincronização na rede local", 20), syncStatus, sync,
                Ui.Text("Sincroniza favoritos, episódios ouvidos e posição de reprodução com a mesma lista e perfil. A outra aplicação deve estar aberta na mesma rede.", 13, true))),
            new NearbySyncView()
        );
        body.Padding = 16;
        Content = new ScrollView { Content = body };
    }
    private void RefreshSyncStatus() => syncStatus.Text = FavoritesSyncService.Status;
}
