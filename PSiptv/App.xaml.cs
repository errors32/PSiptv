using PSiptv.Services;
using PSiptv.Views;

namespace PSiptv;

public partial class App : Application
{
    public App()
    {
        // Enable the newly automatic floating player once for existing installs.
        // Afterwards the setting remains under the user's control.
        if (!Preferences.Default.Get("pipAutomaticDefaultApplied", false))
        {
            Preferences.Default.Set("pip", true);
            Preferences.Default.Set("pipAutomaticDefaultApplied", true);
        }
        if (!Preferences.Default.ContainsKey("subtitleMode"))
            Preferences.Default.Set("subtitleMode", Preferences.Default.Get("subtitles", true) ? "foreign" : "off");
        LanguageService.Apply();
        InitializeComponent();
        ThemeService.Apply();
        RequestedThemeChanged += (_, _) =>
        {
            if (ThemeService.Mode == ThemeMode.System) ThemeService.RefreshColors();
        };
        PlaybackService.CleanOldPlaylists();
        TimeshiftService.Cleanup();
        EpgService.StartBackgroundUpdates();
        CatalogUpdateService.ConfigureSchedule(false);
        AppServices.Client.UserAgent = AppOptions.UserAgent;
        if (AppOptions.AutoClearCache) CacheService.Clear();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var mainPage = DeviceProfile.IsAutomotive ? null : new MainPage();
        var navigation = new NavigationPage((Page?)mainPage ?? new AutomotivePage());
        navigation.SetDynamicResource(NavigationPage.BarBackgroundColorProperty, "Surface");
        navigation.SetDynamicResource(NavigationPage.BarTextColorProperty, "Ink");
        var window = new Window(navigation) { Title = "PSiptv" };
        if (mainPage is null) _ = CatalogUpdateService.TryRunDueAsync(true);
        window.Stopped += (_, _) =>
        {
            if (PictureInPictureService.IsActive) return;
            RemoteControlService.Stop();
        };
        ReminderService.Due += reminder => Dispatcher.Dispatch(async () =>
        {
            var page = Windows.FirstOrDefault()?.Page;
            if (page is not null)
                await LanguageService.AlertAsync(page, "Lembrete de programa",
                    $"{reminder.ProgrammeTitle} · {reminder.ChannelName}");
        });
        window.Resumed += (_, _) =>
        {
            EpgService.RefreshActiveInBackground();
            // Returning from Android PiP resumes the same playback activity.
            // Some phones report the PiP callback just before this event; do
            // not pop the player page and dispose its video in that interval.
            // Tablets keep their existing, already working lifecycle path.
            if (PictureInPictureService.ShouldPreservePlaybackOnResume()) return;
            if (mainPage is not null)
                Dispatcher.Dispatch(async () =>
                {
                    await mainPage.ResumeAsync();
                    await mainPage.OpenPendingNotificationAsync();
                    _ = CatalogUpdateService.TryRunDueAsync(false);
                });
        };
        return window;
    }
}

