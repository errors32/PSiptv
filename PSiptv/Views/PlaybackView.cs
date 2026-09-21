using PSiptv.Core;
using PSiptv.Services;
#if ANDROID
using LibVLCSharp.Shared;
using LibVLCSharp.MAUI;
#else
using CommunityToolkit.Maui.Views;
#endif

namespace PSiptv.Views;

// Both the live preview and the full player use the same playback preferences.
public sealed class PlaybackView : ContentView
{
    public Action? ToggleFullscreen { get; set; }
    public event EventHandler? MediaOpened;
    public event EventHandler? MediaFailed;
    public event EventHandler? MediaEnded;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ControlsVisibilityChanged;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly bool compactMode;
    private bool previewMode;
    private readonly bool recordHistory;
    private readonly bool requireActiveAccount;
    private int generation;
    private MediaItem? current;
    private MediaItem? lastRequested;
    private string accountId = "";
    private int session;
    private int ticks;
    private int volume = 100;
    private bool keepScreenOn;
    private int reconnectAttempt;
    private CancellationTokenSource? reconnecting;
    private double recoveryPosition;
    private readonly IDispatcherTimer timer;
    private readonly Label externalSubtitle = Ui.Text("", 24);
    private IReadOnlyList<SubtitleCue> externalCues = [];
    private bool externalSubtitlesEnabled;
#if ANDROID
    private readonly VideoView video = new()
    {
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill
    };
    private LibVLC? engine;
    private MediaPlayer? player;
    private Media? media;
    private readonly Label rate = Ui.Text("", 12);
    private readonly TransferRateEstimator inputTransferRate = new();
    private readonly TransferRateEstimator demuxTransferRate = new();
    private readonly Slider seek = new() { Minimum = 0, Maximum = 1 };
    private readonly Button pause;
    private readonly Button aspect;
    private readonly Button audio;
    private readonly Button subtitles;
    private readonly Button rewindLive;
    private readonly Button forwardLive;
    private readonly Button returnToLive;
    private readonly Label timeshiftStatus;
    private readonly HorizontalStackLayout timeshiftControls;
    private readonly Grid controls;
    private CancellationTokenSource? controlsHiding;
    private CancellationTokenSource? pipSurfaceRefreshing;
    private AndroidVideoSurfaceCallback? androidVideoSurfaceCallback;
    private LibVLCSharp.Platforms.Android.VideoView? observedNativeVideo;
    private long phonePipSurfaceRecoveryUntil;
    private string aspectMode = AppOptions.VideoAspectRatio;
    private string? aspectModeBeforeFullscreen;
    private bool dragging;
    private bool opened, ended, failed;
    private bool trackPreferencesApplied;
    private long bufferingSince;
    private VLCState? observedState;
    private int timeshiftOffsetSeconds;
    private long? timeshiftPausedAt;
    public double Position => (player?.Time ?? 0) / 1000d;
    public bool IsPlaying => player?.IsPlaying == true;
#else
    private readonly MediaElement video = new() { ShouldAutoPlay = true, ShouldShowPlaybackControls = true, Aspect = Aspect.AspectFit };
    private Aspect? aspectBeforeFullscreen;
    public double Position => video.Position.TotalSeconds;
    public bool IsPlaying => current is not null;
#endif
    public bool AreControlsVisible =>
#if ANDROID
        controls.IsVisible;
#else
        true;
#endif

    public void HideControls()
    {
#if ANDROID
        controlsHiding?.Cancel();
        SetControlsVisible(false);
#endif
    }

    public void SetPreviewMode(bool value)
    {
        previewMode = value;
#if ANDROID
        if (value)
        {
            controlsHiding?.Cancel();
            SetControlsVisible(false);
        }
        else ShowControlsTemporarily();
#else
        video.ShouldShowPlaybackControls = !value && !compactMode;
#endif
    }

    public int Volume
    {
        get =>
#if ANDROID
            player?.Volume ?? volume;
#else
            volume;
#endif
        set
        {
            volume = Math.Clamp(value, 0, 100);
#if ANDROID
            if (player is not null) player.Volume = volume;
#else
            video.Volume = volume / 100d;
#endif
        }
    }

    private void NotifyControlsVisibilityChanged(bool visible) => ControlsVisibilityChanged?.Invoke(visible);
    public PlaybackView(bool compactMode = false, bool? recordHistory = null, bool requireActiveAccount = true)
    {
        this.compactMode = compactMode;
        this.recordHistory = recordHistory ?? !compactMode;
        this.requireActiveAccount = requireActiveAccount;
        BackgroundColor = Colors.Black;
        externalSubtitle.IsVisible = false;
        externalSubtitle.InputTransparent = true;
        externalSubtitle.HorizontalOptions = LayoutOptions.Center;
        externalSubtitle.VerticalOptions = LayoutOptions.End;
        externalSubtitle.HorizontalTextAlignment = TextAlignment.Center;
        externalSubtitle.TextColor = Colors.White;
        externalSubtitle.BackgroundColor = Color.FromArgb("#B3000000");
        externalSubtitle.Padding = new Thickness(10, 5);
        externalSubtitle.Margin = new Thickness(20, 20, 20, 84);
        externalSubtitle.MaximumWidthRequest = 1000;
#if ANDROID
        var grid = new Grid();
        grid.Add(video);
        grid.Add(externalSubtitle);
        pause = Ui.Button("", TogglePlaybackAsync);
        pause.ImageSource = Ui.FontIconSource(FaIcons.Play, 20);
        pause.WidthRequest = 48;
        pause.Padding = 10;
        SemanticProperties.SetDescription(pause, LanguageService.Text("Reproduzir ou pausar"));
        aspect = Ui.Button("Proporção", SelectAspectRatioAsync);
        aspect.ImageSource = Ui.FontIconSource(FaIcons.CropSimple, 20);
        aspect.Padding = new Thickness(10, 4);
        UpdateAspectDescription();
        audio = Ui.Button("Áudio", SelectAudioAsync);
        audio.ImageSource = Ui.FontIconSource(FaIcons.VolumeHigh, 20);
        audio.Padding = new Thickness(10, 4);
        audio.IsVisible = false;
        SemanticProperties.SetDescription(audio, LanguageService.Text("Escolher faixa de áudio"));
        subtitles = Ui.Button("Legendas", SelectSubtitlesAsync);
        subtitles.ImageSource = Ui.FontIconSource(FaIcons.ClosedCaptioning, 20);
        subtitles.Padding = new Thickness(10, 4);
        subtitles.IsVisible = false;
        SemanticProperties.SetDescription(subtitles, LanguageService.Text("Escolher faixa de legendas"));
        rewindLive = TimeshiftButton("−30", RewindTimeshiftAsync, "Recuar 30 segundos");
        forwardLive = TimeshiftButton("+30", ForwardTimeshiftAsync, "Avançar 30 segundos");
        returnToLive = TimeshiftButton("DIRETO", ReturnToLiveAsync, "Voltar ao direto");
        timeshiftStatus = Ui.Text("DIRETO", 12, true);
        timeshiftStatus.TextColor = Colors.White;
        timeshiftStatus.VerticalTextAlignment = TextAlignment.Center;
        timeshiftControls = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children = { rewindLive, forwardLive, returnToLive, timeshiftStatus }
        };
        rate.TextColor = Colors.White;
        controls = new Grid
        {
            Padding = new Thickness(8, 4),
            ColumnSpacing = 8,
            RowSpacing = 4,
            VerticalOptions = LayoutOptions.End,
            ColumnDefinitions = [new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)]
        };
        var playbackProgress = Ui.Stack(seek, rate);
        playbackProgress.Spacing = 0;
        controls.Add(playbackProgress);
        var actionBar = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Children = { pause, aspect, audio, subtitles, timeshiftControls }
        };
        var actionScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = actionBar,
            VerticalOptions = LayoutOptions.End
        };
        controls.Add(actionScroll, 0, 1);
        grid.Add(controls);
        Content = grid;
        if (!compactMode)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => ShowControlsTemporarily();
            video.GestureRecognizers.Add(tap);
        }
        seek.DragStarted += (_, _) => { dragging = true; KeepControlsVisible(); };
        seek.DragCompleted += (_, _) => { if (player is not null) player.Time = (long)(seek.Value * 1000); dragging = false; ShowControlsTemporarily(); };
        void PipChanged()
        {
            if (PictureInPictureService.IsActive) { controlsHiding?.Cancel(); SetControlsVisible(false); }
            else ShowControlsTemporarily();
        }
        Loaded += (_, _) =>
        {
            PictureInPictureService.Changed += PipChanged;
            EnsureAndroidVideoSurfaceCallback();
            PipChanged();
            if (compactMode || previewMode) SetControlsVisible(false);
            else ShowControlsTemporarily();
        };
        Unloaded += (_, _) =>
        {
            PictureInPictureService.Changed -= PipChanged;
            controlsHiding?.Cancel();
            if (!PictureInPictureService.IsActive)
            {
                pipSurfaceRefreshing?.Cancel();
                DetachAndroidVideoSurfaceCallback();
            }
        };
        SizeChanged += (_, _) =>
        {
            RefreshNativeVideoLayout();
            ApplyAspectRatio();
            _ = ReapplyAspectRatioAfterLayoutAsync(generation);
        };
#else
        video.ShouldShowPlaybackControls = !compactMode;
        var grid = new Grid();
        grid.Add(video);
        grid.Add(externalSubtitle);
        Content = grid;
        video.MediaOpened += async (_, _) => { PlaybackOpened(); MediaOpened?.Invoke(this, EventArgs.Empty); if (this.recordHistory && current is { } item) { try { await HistoryService.RecordAsync(accountId, session, item, Position); } catch { } } };
        video.MediaFailed += (_, _) => _ = HandlePlaybackFailureAsync("O leitor não conseguiu abrir a transmissão.");
        video.MediaEnded += (_, _) => { SetKeepScreenOn(false); MediaEnded?.Invoke(this, EventArgs.Empty); };
#endif
        timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += async (_, _) =>
        {
#if ANDROID
            // Poll from the UI thread. Reverse P/Invoke events on VLC-owned
            // Android threads conflict with JNI detach during native shutdown.
            if (player?.State == VLCState.Playing && !opened)
            {
                opened = true;
                PlaybackOpened();
                ApplyTrackPreferences();
                ApplyAspectRatio();
                _ = ReapplyAspectRatioAfterLayoutAsync(generation);
                _ = RefreshVideoSurfaceAfterOpenAsync(generation);
                MediaOpened?.Invoke(this, EventArgs.Empty);
                if (this.recordHistory && current is { } first) { try { await HistoryService.RecordAsync(accountId, session, first, Position); } catch { } }
            }
            if (opened && !trackPreferencesApplied) ApplyTrackPreferences();
            ObserveAndroidStreamState();
            if (player?.State == VLCState.Ended && !ended) { ended = true; SetKeepScreenOn(false); MediaEnded?.Invoke(this, EventArgs.Empty); }
            if (player is not null && !dragging) { seek.Maximum = Math.Max(1, player.Length / 1000d); seek.Value = Math.Clamp(Position, 0, seek.Maximum); seek.IsEnabled = player.IsSeekable; }
            UpdateTimeshiftControls();
            UpdatePlaybackButton();
            rate.IsVisible = AppOptions.NetworkSpeed;
            if (media is not null && rate.IsVisible)
            {
                var statistics = media.Statistics;
                var timestamp = Environment.TickCount64;
                // LibVLC's instantaneous bitrate is allowed to be zero while
                // its input buffer is full. Derive the rate from both byte
                // counters and use the reported value only as a fallback.
                var input = inputTransferRate.Sample(unchecked((uint)statistics.ReadBytes), timestamp);
                var demux = demuxTransferRate.Sample(unchecked((uint)statistics.DemuxReadBytes), timestamp);
                var reported = Math.Max(statistics.InputBitrate, statistics.DemuxBitrate) * 8d;
                var megabits = Math.Max(reported, Math.Max(input, demux));
                rate.Text = LanguageService.Format("{0:F2} Mbps · transmissão", megabits);
            }
#endif
            UpdateExternalSubtitle();
            if (this.recordHistory && ++ticks % 40 == 0 && current is { } item && IsPlaying)
            { try { await HistoryService.RecordAsync(accountId, session, item, Position); } catch { /* Playback must survive storage exhaustion. */ } }
            if (current is { Kind: not MediaKind.Channel } && IsPlaying)
                recoveryPosition = Math.Max(recoveryPosition, Position);
        };
        AppServices.PlaybackSuspended += Stop;
        Unloaded += (_, _) =>
        {
            // Android can temporarily unload the MAUI view while moving the
            // activity into picture-in-picture. Releasing VLC here leaves the
            // floating window open with a stopped/blank video.
            if (PictureInPictureService.IsActive) return;
            AppServices.PlaybackSuspended -= Stop;
            Stop();
        };
    }

    public async Task PlayAsync(MediaItem item, double resume = 0)
    {
        CancelReconnect();
        reconnectAttempt = 0;
        recoveryPosition = resume;
        lastRequested = item;
        await StartPlaybackAsync(item, resume);
    }

    public async Task SelectExternalSubtitlesAsync(Page page)
    {
        var choices = new List<string> { "Escolher ficheiro SRT/VTT", "Adicionar legendas por URL" };
        if (externalCues.Count > 0)
            choices.Add(externalSubtitlesEnabled ? "Desligar legendas externas" : "Ligar legendas externas");
        var selected = await LanguageService.ActionSheetAsync(page, "Legendas externas", "Cancelar", null, choices.ToArray());
        if (selected == "Escolher ficheiro SRT/VTT")
        {
            var cues = await ExternalSubtitleService.PickAsync(CancellationToken.None);
            if (cues.Count == 0) return;
            EnableExternalSubtitles(cues);
        }
        else if (selected == "Adicionar legendas por URL")
        {
            var url = await page.DisplayPromptAsync(LanguageService.Text("Legendas externas"),
                LanguageService.Text("Endereço HTTP ou HTTPS de um ficheiro SRT ou WebVTT."),
                LanguageService.Text("Carregar"), LanguageService.Text("Cancelar"), keyboard: Keyboard.Url,
                maxLength: 2048);
            if (string.IsNullOrWhiteSpace(url)) return;
            EnableExternalSubtitles(await ExternalSubtitleService.LoadUrlAsync(url, CancellationToken.None));
        }
        else if (selected == "Desligar legendas externas")
        {
            externalSubtitlesEnabled = false;
            externalSubtitle.IsVisible = false;
        }
        else if (selected == "Ligar legendas externas")
        {
            externalSubtitlesEnabled = true;
#if ANDROID
            player?.SetSpu(-1);
#endif
            UpdateExternalSubtitle();
        }
        StatusChanged?.Invoke(LanguageService.Text(externalSubtitlesEnabled
            ? "Legendas externas ativas." : "Legendas externas desligadas."));
    }

    private void EnableExternalSubtitles(IReadOnlyList<SubtitleCue> cues)
    {
        externalCues = cues;
        externalSubtitlesEnabled = true;
#if ANDROID
        player?.SetSpu(-1);
#endif
        UpdateExternalSubtitle();
    }

    private void UpdateExternalSubtitle()
    {
        if (!externalSubtitlesEnabled || externalCues.Count == 0 || current is null)
        {
            externalSubtitle.IsVisible = false;
            return;
        }
        var seconds = Math.Max(0, Position - AppOptions.SubtitleDelayMs / 1000d);
        var text = ExternalSubtitleParser.TextAt(externalCues, TimeSpan.FromSeconds(seconds));
        if (externalSubtitle.Text != text) externalSubtitle.Text = text;
        externalSubtitle.IsVisible = text.Length > 0;
    }

    public Task RetryAsync() => (current ?? lastRequested) is { } item
        ? PlayAsync(item, item.Kind == MediaKind.Channel ? 0 : Math.Max(recoveryPosition, Position))
        : Task.CompletedTask;

    private async Task StartPlaybackAsync(MediaItem item, double resume)
    {
        if (current is not null && CatalogPreferences.ItemKey(current) != CatalogPreferences.ItemKey(item))
        {
            externalCues = [];
            externalSubtitlesEnabled = false;
            externalSubtitle.IsVisible = false;
        }
        var request = ++generation;
        var account = AppServices.ActiveAccount;
        if (account is null && requireActiveAccount) return;
        var version = AppServices.SessionVersion;
        var source = account is null ? item : StreamPreferences.ApplyFormat(account, item, AppOptions.StreamFormat);
        if (account is not null) source = await AppServices.Client.ResolveStreamAsync(account, source, CancellationToken.None);
        var sourceUri = new Uri(source.Url, UriKind.Absolute);
        if (!sourceUri.IsFile && sourceUri.Scheme != "content") WebAddress.Require(source.Url);
        await gate.WaitAsync();
        try
        {
            await ReleaseAsync();
            if (request != generation || version != AppServices.SessionVersion) return;
            current = item; accountId = account?.Id ?? ""; session = version;
#if ANDROID
            inputTransferRate.Reset();
            demuxTransferRate.Reset();
            rate.Text = "";
            var options = new List<string> { "--no-video-title-show" };
            if (AppOptions.OpenSl) options.Add("--aout=opensles");
            if (AppOptions.OpenGl) options.Add("--vout=gles2");
            var timeshiftEnabled = !compactMode && item.Kind == MediaKind.Channel && AppOptions.TimeshiftEnabled;
            if (timeshiftEnabled)
            {
                options.Add($"--input-timeshift-path={TimeshiftService.PrepareDirectory()}");
                options.Add("--input-timeshift-granularity=1048576");
            }
            engine = new LibVLC(options.ToArray());
            player = new MediaPlayer(engine) { EnableHardwareDecoding = AppOptions.Decoder != "software" };
            player.Volume = volume;
            media = new Media(engine, sourceUri);
            media.AddOption($":network-caching={AppOptions.BufferSeconds * 1000}");
            media.AddOption($":live-caching={AppOptions.BufferSeconds * 1000}");
            AddHttpOption(media, "http-user-agent", FirstHttpValue(item.HttpUserAgent, AppOptions.UserAgent));
            AddHttpOption(media, "http-referrer", item.HttpReferer);
            AddHttpOption(media, "http-cookie", item.HttpCookie);
            if (AppOptions.Decoder == "hardware") media.AddOption(":avcodec-hw=mediacodec");
            if (AppOptions.Decoder == "software") media.AddOption(":avcodec-hw=none");
            if (!AppOptions.Subtitles) media.AddOption(":no-spu");
            if (resume > 0 && item.Kind != MediaKind.Channel) media.AddOption($":start-time={resume.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            opened = ended = failed = trackPreferencesApplied = false;
            timeshiftOffsetSeconds = 0;
            timeshiftPausedAt = null;
            timeshiftControls.IsVisible = timeshiftEnabled;
            seek.IsVisible = !timeshiftEnabled;
            bufferingSince = 0;
            observedState = null;
            video.MediaPlayer = player;
            EnsureAndroidVideoSurfaceCallback();
            player.Play(media);
            UpdatePlaybackButton();
            ApplyAspectRatio();
            ShowControlsTemporarily();
#else
            video.Source = sourceUri.IsFile ? MediaSource.FromFile(sourceUri.LocalPath) : MediaSource.FromUri(source.Url);
            if (resume > 0) await video.SeekTo(TimeSpan.FromSeconds(resume));
#endif
            if (!compactMode) PictureInPictureService.Current = this;
            SetKeepScreenOn(true);
            ticks = 0; timer.Start();
        }
        finally { gate.Release(); }
    }

#if ANDROID
    private static string FirstHttpValue(string? preferred, string fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;

    private static void AddHttpOption(Media target, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        // HTTP header values must remain a single line. These values originate
        // in WebView, but stripping control characters also keeps VLC options
        // well formed if a provider returns a malformed value.
        var clean = value.Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal).Trim();
        if (clean.Length > 0) target.AddOption($":{name}={clean}");
    }
#endif

    public async void Stop() => await StopCoreAsync();

    public Task StopForExternalPlaybackAsync() => StopCoreAsync();

    private async Task StopCoreAsync()
    {
        CancelReconnect();
        reconnectAttempt = 0;
        ++generation;
#if ANDROID
        controlsHiding?.Cancel();
#endif
        timer.Stop();
        if (PictureInPictureService.Current == this) PictureInPictureService.Current = null;
        await gate.WaitAsync();
        try { await ReleaseAsync(); } finally { gate.Release(); }
    }

    public void Pause()
    {
#if ANDROID
        if (player?.IsPlaying == true)
        {
            player.Pause();
            if (IsTimeshiftChannel) timeshiftPausedAt = Environment.TickCount64;
        }
        UpdatePlaybackButton();
#else
        video.Pause();
#endif
    }

    public void HandlePictureInPictureChanged(bool active)
    {
#if ANDROID
        // Android resizes and, on some devices, recreates the SurfaceView while
        // entering or leaving PiP. LibVLC can remain attached to the old surface,
        // so audio continues while the new floating window stays black.
        if (DeviceInfo.Idiom == DeviceIdiom.Phone)
            phonePipSurfaceRecoveryUntil = Environment.TickCount64 + 10000;
        pipSurfaceRefreshing?.Cancel();
        var refresh = new CancellationTokenSource();
        pipSurfaceRefreshing = refresh;
        _ = RefreshVideoSurfaceForPictureInPictureAsync(refresh, generation, returningToApplication: !active);
#endif
    }

#if ANDROID
    public void ConfigurePictureInPicture(Android.App.PictureInPictureParams.Builder builder)
    {
        // Restrict the PiP transition to the actual video instead of shrinking
        // the complete page (status/header included). The final PiP activity is
        // already switched to a video-only layout by LiveTvView/PlayerPage.
        if (video.Handler?.PlatformView is not Android.Views.View native) return;
        using var bounds = new Android.Graphics.Rect();
        if (native.GetGlobalVisibleRect(bounds) && bounds.Width() > 0 && bounds.Height() > 0)
            builder.SetSourceRectHint(bounds);
    }
#endif

    public void SetFullscreen(bool value)
    {
#if ANDROID
        subtitles.IsVisible = value;
        if (value)
        {
            aspectModeBeforeFullscreen ??= aspectMode;
            aspectMode = AppOptions.FullscreenVideoAspectRatio;
        }
        else if (aspectModeBeforeFullscreen is { } previous)
        {
            aspectMode = previous;
            aspectModeBeforeFullscreen = null;
        }
        ApplyAspectRatio();
        _ = ReapplyAspectRatioAfterLayoutAsync(generation);
#else
        if (value)
        {
            aspectBeforeFullscreen ??= video.Aspect;
            video.Aspect = AppOptions.FullscreenVideoAspectRatio switch { "fill" => Aspect.AspectFill, "stretch" => Aspect.Fill, _ => Aspect.AspectFit };
        }
        else if (aspectBeforeFullscreen is { } previous)
        {
            video.Aspect = previous;
            aspectBeforeFullscreen = null;
        }
#endif
    }

    private void PlaybackOpened()
    {
        reconnectAttempt = 0;
#if ANDROID
        bufferingSince = 0;
#endif
        SetKeepScreenOn(true);
    }

    private async Task HandlePlaybackFailureAsync(string diagnosis)
    {
        if (current is not { } item || reconnecting is not null) return;
        SetKeepScreenOn(false);
        if (reconnectAttempt >= StreamRecoveryPolicy.MaxAttempts)
        {
            StatusChanged?.Invoke(LanguageService.Format(
                "Falha do stream após {0} tentativas · {1}", StreamRecoveryPolicy.MaxAttempts, diagnosis));
            MediaFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var attempt = ++reconnectAttempt;
        var delay = StreamRecoveryPolicy.DelayForAttempt(attempt);
        if (item.Kind != MediaKind.Channel)
            recoveryPosition = Math.Max(recoveryPosition, Position);
        var resume = item.Kind == MediaKind.Channel ? 0 : recoveryPosition;
        var request = generation;
        var source = new CancellationTokenSource();
        reconnecting = source;
        StatusChanged?.Invoke(LanguageService.Format(
            "{0} Nova tentativa {1} de {2} em {3} s…", diagnosis, attempt,
            StreamRecoveryPolicy.MaxAttempts, (int)delay.TotalSeconds));
        try
        {
            await Task.Delay(delay, source.Token);
            if (request != generation || current != item) return;
            StatusChanged?.Invoke(LanguageService.Format(
                "A restabelecer transmissão · tentativa {0} de {1}…", attempt,
                StreamRecoveryPolicy.MaxAttempts));
            await StartPlaybackAsync(item, resume);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            StatusChanged?.Invoke(LanguageService.Format(
                "Falha do stream após {0} tentativas · O leitor não conseguiu abrir a transmissão.", attempt));
            MediaFailed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (ReferenceEquals(reconnecting, source)) reconnecting = null;
            source.Dispose();
        }
    }

    private void CancelReconnect()
    {
        var source = reconnecting;
        reconnecting = null;
        if (source is null) return;
        source.Cancel();
        source.Dispose();
    }

#if ANDROID
    private void ObserveAndroidStreamState()
    {
        if (player is not { } active || current is null) return;
        var state = active.State;
        if (state != observedState)
        {
            observedState = state;
            if (state == VLCState.Opening)
            {
                bufferingSince = Environment.TickCount64;
                StatusChanged?.Invoke(LanguageService.Text("A abrir stream…"));
            }
            else if (state == VLCState.Buffering)
            {
                bufferingSince = Environment.TickCount64;
                StatusChanged?.Invoke(LanguageService.Text("A receber dados da transmissão…"));
            }
            else if (state == VLCState.Playing)
                bufferingSince = 0;
        }

        if (state == VLCState.Error && !failed)
        {
            failed = true;
            bufferingSince = 0;
            _ = HandlePlaybackFailureAsync(LanguageService.Text("O leitor indicou um erro no stream."));
        }
        else if ((state is VLCState.Opening or VLCState.Buffering) && !failed && bufferingSince > 0 &&
                 Environment.TickCount64 - bufferingSince >= StreamRecoveryPolicy.BufferingTimeout.TotalMilliseconds)
        {
            failed = true;
            _ = HandlePlaybackFailureAsync(LanguageService.Text("A transmissão ficou sem receber dados."));
        }
    }
#endif

    public void UseStretchToViewport()
    {
#if ANDROID
        aspectMode = "stretch";
        ApplyAspectRatio();
        _ = ReapplyAspectRatioAfterLayoutAsync(generation);
#else
        video.Aspect = Aspect.Fill;
#endif
    }

    private void SetKeepScreenOn(bool value)
    {
        if (compactMode) return;
        if (keepScreenOn == value) return;
        keepScreenOn = value;
        try { DeviceDisplay.Current.KeepScreenOn = value; }
        catch (FeatureNotSupportedException) { }
    }
#if ANDROID
    private void KeepControlsVisible()
    {
        controlsHiding?.Cancel();
        controlsHiding?.Dispose();
        controlsHiding = null;
        SetControlsVisible(true);
    }

    private void ShowControlsTemporarily()
    {
        if (compactMode || previewMode) { SetControlsVisible(false); return; }
        KeepControlsVisible();
        if (PictureInPictureService.IsActive) { SetControlsVisible(false); return; }
        var source = new CancellationTokenSource();
        controlsHiding = source;
        _ = HideControlsAsync(source);
    }

    private async Task HideControlsAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), source.Token);
            if (ReferenceEquals(controlsHiding, source) && !dragging) SetControlsVisible(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(controlsHiding, source)) controlsHiding = null;
            source.Dispose();
        }
    }

    private void SetControlsVisible(bool visible)
    {
        controls.IsVisible = visible;
        NotifyControlsVisibilityChanged(visible);
    }

    private void UpdatePlaybackButton()
    {
        var playing = player?.IsPlaying == true;
        pause.ImageSource = Ui.FontIconSource(playing ? FaIcons.Pause : FaIcons.Play, 20);
        SemanticProperties.SetDescription(pause, LanguageService.Text(playing ? "Pausar" : "Reproduzir"));
    }

    private bool IsTimeshiftChannel => current?.Kind == MediaKind.Channel && AppOptions.TimeshiftEnabled;

    private Button TimeshiftButton(string text, Func<Task> action, string description)
    {
        var button = Ui.Button(text, action);
        button.FontSize = 12;
        button.MinimumHeightRequest = 36;
        button.Padding = new Thickness(8, 3);
        SemanticProperties.SetDescription(button, LanguageService.Text(description));
        return button;
    }

    private async Task TogglePlaybackAsync()
    {
        if (player is null)
        {
            if (lastRequested is { } item) await PlayAsync(item, item.Kind == MediaKind.Channel ? 0 : recoveryPosition);
            return;
        }
        if (player.IsPlaying)
        {
            if (IsTimeshiftChannel && !player.CanPause)
            {
                StatusChanged?.Invoke(LanguageService.Text("Timeshift não está disponível neste stream."));
                ShowControlsTemporarily();
                return;
            }
            player.Pause();
            if (IsTimeshiftChannel) timeshiftPausedAt = Environment.TickCount64;
        }
        else
        {
            CommitPausedTimeshiftOffset();
            player.Play();
        }
        UpdateTimeshiftControls();
        UpdatePlaybackButton();
        ShowControlsTemporarily();
    }

    private Task RewindTimeshiftAsync()
    {
        if (!CanSeekTimeshift()) return Task.CompletedTask;
        player!.Time = Math.Max(0, player.Time - TimeshiftPolicy.SkipSeconds * 1000L);
        timeshiftOffsetSeconds = TimeshiftPolicy.ClampOffset(
            EffectiveTimeshiftOffset(), TimeshiftPolicy.SkipSeconds, AppOptions.TimeshiftMinutes);
        timeshiftPausedAt = player.IsPlaying ? null : Environment.TickCount64;
        UpdateTimeshiftControls();
        ShowControlsTemporarily();
        return Task.CompletedTask;
    }

    private async Task ForwardTimeshiftAsync()
    {
        if (!CanSeekTimeshift()) return;
        var offset = EffectiveTimeshiftOffset();
        if (offset <= TimeshiftPolicy.SkipSeconds)
        {
            await ReturnToLiveAsync();
            return;
        }
        player!.Time = Math.Min(player.Length > 0 ? player.Length : long.MaxValue,
            player.Time + TimeshiftPolicy.SkipSeconds * 1000L);
        timeshiftOffsetSeconds = TimeshiftPolicy.ClampOffset(
            offset, -TimeshiftPolicy.SkipSeconds, AppOptions.TimeshiftMinutes);
        timeshiftPausedAt = player.IsPlaying ? null : Environment.TickCount64;
        UpdateTimeshiftControls();
        ShowControlsTemporarily();
    }

    private async Task ReturnToLiveAsync()
    {
        if (current is not { Kind: MediaKind.Channel } item) return;
        timeshiftOffsetSeconds = 0;
        timeshiftPausedAt = null;
        StatusChanged?.Invoke(LanguageService.Text("A regressar ao direto…"));
        await PlayAsync(item);
        StatusChanged?.Invoke(LanguageService.Text("Em direto"));
    }

    private bool CanSeekTimeshift()
    {
        if (IsTimeshiftChannel && player?.IsSeekable == true) return true;
        StatusChanged?.Invoke(LanguageService.Text("O fornecedor não permite recuar neste stream."));
        ShowControlsTemporarily();
        return false;
    }

    private int EffectiveTimeshiftOffset()
    {
        var elapsed = timeshiftPausedAt is { } started
            ? (int)Math.Max(0, (Environment.TickCount64 - started) / 1000)
            : 0;
        return TimeshiftPolicy.ClampOffset(timeshiftOffsetSeconds, elapsed, AppOptions.TimeshiftMinutes);
    }

    private void CommitPausedTimeshiftOffset()
    {
        if (timeshiftPausedAt is null) return;
        timeshiftOffsetSeconds = EffectiveTimeshiftOffset();
        timeshiftPausedAt = null;
    }

    private void UpdateTimeshiftControls()
    {
        if (!IsTimeshiftChannel)
        {
            timeshiftControls.IsVisible = false;
            return;
        }
        timeshiftControls.IsVisible = true;
        var offset = EffectiveTimeshiftOffset();
        if (timeshiftPausedAt is not null && offset >= AppOptions.TimeshiftMinutes * 60)
        {
            CommitPausedTimeshiftOffset();
            player?.Play();
            StatusChanged?.Invoke(LanguageService.Text("Janela máxima atingida. A reprodução foi retomada."));
        }
        timeshiftStatus.Text = offset == 0
            ? LanguageService.Text("DIRETO")
            : TimeshiftPolicy.FormatOffset(offset);
        var seekable = player?.IsSeekable == true;
        rewindLive.IsEnabled = seekable;
        forwardLive.IsEnabled = seekable && offset > 0;
        returnToLive.IsEnabled = offset > 0 || player?.IsPlaying != true;
    }

    private async Task SelectAspectRatioAsync()
    {
        KeepControlsVisible();
        var page = FindParentPage();
        if (page is null) return;
        var choices = new[] { "Ajustar", "Preencher ecrã", "Esticar ao ecrã", "16:9", "4:3", "10:9", "21:9" };
        var selected = await LanguageService.ActionSheetAsync(page, "Proporção do ecrã", "Cancelar", null, choices);
        var index = Array.IndexOf(choices, selected);
        if (index >= 0)
        {
            aspectMode = index switch { 0 => "fit", 1 => "fill", 2 => "stretch", _ => choices[index] };
            Preferences.Default.Set(aspectModeBeforeFullscreen is null ? "videoAspectRatio" : "fullscreenVideoAspectRatio", aspectMode);
            ApplyAspectRatio();
            UpdateAspectDescription();
        }
        ShowControlsTemporarily();
    }

    private void ApplyAspectRatio()
    {
        var active = player;
        if (active is null) return;
        var viewportRatio = ViewportAspectRatio();
        active.Scale = 0;
        active.AspectRatio = null;
        active.CropGeometry = "";
        if (aspectMode == "fill" && viewportRatio is not null) active.CropGeometry = viewportRatio;
        else if (aspectMode == "stretch" && viewportRatio is not null) active.AspectRatio = viewportRatio;
        else if (aspectMode is "16:9" or "4:3" or "10:9" or "21:9") active.AspectRatio = aspectMode;
    }

    private async Task ReapplyAspectRatioAfterLayoutAsync(int request)
    {
        // The Android surface is recreated both when playback starts and when
        // the device rotates. VLC can reset its display aspect while that is
        // happening, so reapply it throughout the short layout/attach window.
        foreach (var delay in new[] { 80, 180, 350, 700 })
        {
            await Task.Delay(delay);
            if (request != generation || current is null) return;
            await Dispatcher.DispatchAsync(() =>
            {
                ResetNativeVideoToHostBounds();
                RefreshNativeVideoLayout();
                ApplyAspectRatio();
            });
        }
    }

    private async Task RefreshVideoSurfaceAfterOpenAsync(int request)
    {
        // VLC's Android view exposes this specifically for hosts such as MAUI:
        // the first native layout notification can be missed, leaving the
        // SurfaceView at the decoded video's size in the bottom-left corner.
        // Trigger it while the preview settles instead of detaching the player,
        // which recreates the surface but can preserve the same stale bounds.
        foreach (var delay in new[] { 0, 80, 180, 350, 700 })
        {
            if (delay > 0) await Task.Delay(delay);
            if (request != generation || current is null || player is null) return;
            await Dispatcher.DispatchAsync(() =>
            {
                RefreshNativeVideoLayout();
                ApplyAspectRatio();
            });
        }
    }

    private async Task RefreshVideoSurfaceForPictureInPictureAsync(
        CancellationTokenSource refresh,
        int request,
        bool returningToApplication)
    {
        try
        {
            // Wait until Android has applied the PiP bounds, then force LibVLC
            // to attach its video output to the current native surface. Follow
            // with layout refreshes because OEMs settle PiP in several passes.
            var reattachSurface = DeviceInfo.Idiom == DeviceIdiom.Phone;
            var elapsed = 0;
            foreach (var delay in new[] { 80, 180, 350, 700 })
            {
                await Task.Delay(delay - elapsed, refresh.Token);
                elapsed = delay;
                if (request != generation || current is null || player is null) return;
                await Dispatcher.DispatchAsync(() =>
                {
                    if (request != generation || player is null) return;
                    EnsureAndroidVideoSurfaceCallback();
                    if (reattachSurface && returningToApplication)
                        ResetNativeVideoToHostBounds();
                    // On phones the activity transition can replace the
                    // SurfaceView without LibVLC observing the new surface.
                    // Reattach shortly after the callback. Some phones resize
                    // without recreating SurfaceView, so SurfaceCreated alone
                    // cannot tell us that the attachment became stale.
                    // Keep the tablet path unchanged because its native surface
                    // lifecycle already works correctly.
                    if (reattachSurface && delay == 180 &&
                        video.Handler?.PlatformView is LibVLCSharp.Platforms.Android.VideoView native)
                    {
                        var active = player;
                        native.MediaPlayer = null;
                        native.MediaPlayer = active;
                    }
                    if (reattachSurface && returningToApplication)
                        ResetNativeVideoToHostBounds();
                    RefreshNativeVideoLayout();
                    ApplyAspectRatio();
                });

                if (reattachSurface && delay == 700 && request == generation && current is { } item)
                {
                    // A SurfaceView can remain black even after a successful
                    // reattach because the running VLC vout still owns the old
                    // Android native window. Recreate playback just as changing
                    // channel does, preserving the position for on-demand media.
                    var resume = item.Kind == MediaKind.Channel ? 0 : Position;
                    await PlayAsync(item, resume);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(pipSurfaceRefreshing, refresh)) pipSurfaceRefreshing = null;
            refresh.Dispose();
        }
    }

    private void RefreshNativeVideoLayout()
    {
        if (video.Handler?.PlatformView is not LibVLCSharp.Platforms.Android.VideoView native) return;
        native.TriggerLayoutChangeListener();
        native.ForceLayout();
        native.RequestLayout();
        native.Invalidate();
    }

    private void ResetNativeVideoToHostBounds()
    {
        if (video.Handler?.PlatformView is not LibVLCSharp.Platforms.Android.VideoView native ||
            native.Parent is not Android.Views.View host || host.Width <= 0 || host.Height <= 0) return;

        // SurfaceView may retain the fixed PiP buffer and measured bounds even
        // though MAUI has already restored the containing preview/fullscreen.
        // Restore match-parent sizing and reset the holder to layout-controlled
        // dimensions before notifying VLC of the new window size.
        if (native.LayoutParameters is { } parameters)
        {
            parameters.Width = Android.Views.ViewGroup.LayoutParams.MatchParent;
            parameters.Height = Android.Views.ViewGroup.LayoutParams.MatchParent;
            native.LayoutParameters = parameters;
        }

        native.Holder?.SetSizeFromLayout();
        var left = host.PaddingLeft;
        var top = host.PaddingTop;
        var right = Math.Max(left + 1, host.Width - host.PaddingRight);
        var bottom = Math.Max(top + 1, host.Height - host.PaddingBottom);
        native.Measure(
            Android.Views.View.MeasureSpec.MakeMeasureSpec(right - left, Android.Views.MeasureSpecMode.Exactly),
            Android.Views.View.MeasureSpec.MakeMeasureSpec(bottom - top, Android.Views.MeasureSpecMode.Exactly));
        native.Layout(left, top, right, bottom);
        native.Holder?.SetSizeFromLayout();
    }

    private void EnsureAndroidVideoSurfaceCallback()
    {
        if (video.Handler?.PlatformView is not LibVLCSharp.Platforms.Android.VideoView native ||
            ReferenceEquals(observedNativeVideo, native)) return;

        if (observedNativeVideo is not null && androidVideoSurfaceCallback is not null)
            observedNativeVideo.Holder?.RemoveCallback(androidVideoSurfaceCallback);

        androidVideoSurfaceCallback?.Dispose();
        observedNativeVideo = null;
        androidVideoSurfaceCallback = null;
        if (native.Holder is not { } holder) return;
        observedNativeVideo = native;
        androidVideoSurfaceCallback = new AndroidVideoSurfaceCallback(this, native);
        holder.AddCallback(androidVideoSurfaceCallback);
    }

    private void DetachAndroidVideoSurfaceCallback()
    {
        if (observedNativeVideo is not null && androidVideoSurfaceCallback is not null)
            observedNativeVideo.Holder?.RemoveCallback(androidVideoSurfaceCallback);
        androidVideoSurfaceCallback?.Dispose();
        androidVideoSurfaceCallback = null;
        observedNativeVideo = null;
    }

    private void AndroidVideoSurfaceCreated(LibVLCSharp.Platforms.Android.VideoView native)
    {
        // A phone can create the replacement SurfaceView after all PiP layout
        // callbacks and delayed refreshes have completed. Run after Android has
        // notified every holder callback, then bind VLC to this exact surface.
        native.Post(() =>
        {
            if (DeviceInfo.Idiom != DeviceIdiom.Phone ||
                Environment.TickCount64 > phonePipSurfaceRecoveryUntil || current is null ||
                player is not { } active || !ReferenceEquals(observedNativeVideo, native) ||
                !ReferenceEquals(video.Handler?.PlatformView, native)) return;

            native.MediaPlayer = null;
            native.MediaPlayer = active;
            if (!PictureInPictureService.IsActive) ResetNativeVideoToHostBounds();
            RefreshNativeVideoLayout();
            ApplyAspectRatio();
        });
    }

    private sealed class AndroidVideoSurfaceCallback(
        PlaybackView owner,
        LibVLCSharp.Platforms.Android.VideoView native) : Java.Lang.Object, Android.Views.ISurfaceHolderCallback
    {
        private readonly WeakReference<PlaybackView> owner = new(owner);
        private readonly WeakReference<LibVLCSharp.Platforms.Android.VideoView> native = new(native);

        public void SurfaceCreated(Android.Views.ISurfaceHolder holder)
        {
            if (owner.TryGetTarget(out var playback) && native.TryGetTarget(out var videoView))
                playback.AndroidVideoSurfaceCreated(videoView);
        }

        public void SurfaceChanged(Android.Views.ISurfaceHolder holder, Android.Graphics.Format format, int width, int height)
        {
            if (!owner.TryGetTarget(out var playback) || !native.TryGetTarget(out var videoView)) return;
            videoView.Post(() =>
            {
                if (!ReferenceEquals(playback.observedNativeVideo, videoView)) return;
                if (!PictureInPictureService.IsActive &&
                    Environment.TickCount64 <= playback.phonePipSurfaceRecoveryUntil)
                    playback.ResetNativeVideoToHostBounds();
                playback.RefreshNativeVideoLayout();
                playback.ApplyAspectRatio();
            });
        }

        public void SurfaceDestroyed(Android.Views.ISurfaceHolder holder) { }
    }

    private string? ViewportAspectRatio()
    {
        // Use the stable outer frame. The native video surface can briefly keep
        // its fullscreen dimensions during rotation and feed a distorted ratio
        // back into its own measurement when returning to the preview.
        // Prefer the actual Android host dimensions. MAUI Width/Height can
        // temporarily report the dimensions from the previous orientation.
        var native = video.Handler?.PlatformView as Android.Views.View;
        var width = native?.Width > 0 ? native.Width : (int)Math.Round(Width);
        var height = native?.Height > 0 ? native.Height : (int)Math.Round(Height);
        if (width <= 0 || height <= 0) return null;
        var divisor = GreatestCommonDivisor(width, height);
        return $"{width / divisor}:{height / divisor}";
    }

    private void UpdateAspectDescription()
    {
        var label = aspectMode switch { "fill" => "Preencher ecrã", "stretch" => "Esticar ao ecrã", "fit" => "Ajustar", _ => aspectMode };
        SemanticProperties.SetDescription(aspect, LanguageService.Format("Proporção do ecrã: {0}", LanguageService.Text(label)));
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return Math.Max(1, left);
    }
#endif
    private async Task ReleaseAsync()
    {
        SetKeepScreenOn(false);
        current = null;
#if ANDROID
        pipSurfaceRefreshing?.Cancel();
        timeshiftOffsetSeconds = 0;
        timeshiftPausedAt = null;
        timeshiftControls.IsVisible = false;
        seek.IsVisible = true;
        var old = player; player = null;
        UpdatePlaybackButton();
        if (old is not null) { await Task.Run(old.Stop); video.MediaPlayer = null; old.Dispose(); }
        media?.Dispose(); media = null; engine?.Dispose(); engine = null;
#else
        video.Stop(); video.Source = null;
        await Task.CompletedTask;
#endif
    }
#if ANDROID
    private void ApplyTrackPreferences()
    {
        var active = player;
        if (active is null) return;
        var audioTracks = active.AudioTrackDescription
            .Select(track => new MediaTrackOption(track.Id, track.Name ?? ""))
            .Where(track => track.Id >= 0).ToArray();
        if (audioTracks.Length == 0) return;
        trackPreferencesApplied = true;
        audio.IsVisible = audioTracks.Length > 1;

        var audioLanguage = TrackPreferencePolicy.ResolveLanguage(
            AppOptions.PreferredAudioLanguage, System.Globalization.CultureInfo.CurrentUICulture);
        var rememberedAudio = AppOptions.RememberTrackSelection
            ? Preferences.Default.Get("rememberedAudioTrack", "") : "";
        var selectedAudio = TrackPreferencePolicy.Select(audioTracks, audioLanguage, rememberedAudio);
        if (selectedAudio is not null) active.SetAudioTrack(selectedAudio.Id);

        active.SetAudioDelay(AppOptions.AudioDelayMs * 1000L);
        active.SetSpuDelay(AppOptions.SubtitleDelayMs * 1000L);
        var subtitleTracks = active.SpuDescription
            .Select(track => new MediaTrackOption(track.Id, track.Name ?? ""))
            .Where(track => track.Id >= 0).ToArray();
        subtitles.IsVisible = subtitleTracks.Length > 0;
        if (AppOptions.SubtitleMode == "off" || subtitleTracks.Length == 0)
        {
            active.SetSpu(-1);
            return;
        }

        var rememberedSubtitle = AppOptions.RememberTrackSelection
            ? Preferences.Default.Get("rememberedSubtitleTrack", "") : "";
        if (rememberedSubtitle == "__off__")
        {
            active.SetSpu(-1);
            return;
        }
        var currentAudio = audioTracks.FirstOrDefault(track => track.Id == active.AudioTrack) ?? selectedAudio;
        if (AppOptions.SubtitleMode == "foreign" && currentAudio is not null &&
            TrackPreferencePolicy.MatchesLanguage(currentAudio.Name, audioLanguage))
        {
            active.SetSpu(-1);
            return;
        }

        var subtitleLanguage = TrackPreferencePolicy.ResolveLanguage(
            AppOptions.PreferredSubtitleLanguage, System.Globalization.CultureInfo.CurrentUICulture);
        var selectedSubtitle = TrackPreferencePolicy.Select(subtitleTracks, subtitleLanguage, rememberedSubtitle)
                               ?? subtitleTracks.FirstOrDefault();
        if (selectedSubtitle is not null) active.SetSpu(selectedSubtitle.Id);
    }

    private async Task SelectAudioAsync()
    {
        var active = player;
        var page = FindParentPage();
        if (active is null || page is null) return;
        var tracks = active.AudioTrackDescription.Where(track => track.Id >= 0).ToArray();
        if (tracks.Length == 0)
        {
            await LanguageService.AlertAsync(page, "Áudio", "Este vídeo não contém faixas de áudio selecionáveis.");
            return;
        }
        var labels = tracks.Select(track => track.Name ?? $"Faixa {track.Id}").ToArray();
        var selected = await LanguageService.ActionSheetAsync(page, "Faixa de áudio", "Cancelar", null, labels);
        var index = Array.IndexOf(labels, selected);
        if (index < 0 || active != player) return;
        active.SetAudioTrack(tracks[index].Id);
        if (AppOptions.RememberTrackSelection)
            Preferences.Default.Set("rememberedAudioTrack", tracks[index].Name ?? labels[index]);
        ShowControlsTemporarily();
    }

    private async Task SelectSubtitlesAsync()
    {
        var active = player;
        if (active is null) return;
        var tracks = active.SpuDescription.Where(track => track.Id >= 0).ToArray();
        var page = FindParentPage();
        if (page is null) return;
        if (tracks.Length == 0) { await LanguageService.AlertAsync(page, "Legendas", "Este vídeo não contém faixas de legendas."); return; }
        var labels = new[] { LanguageService.Text("Desligadas") }
            .Concat(tracks.Select(track => track.Name ?? $"Faixa {track.Id}")).ToArray();
        var selected = await LanguageService.ActionSheetAsync(page, "Faixa de legendas", "Cancelar", null, labels);
        var index = Array.IndexOf(labels, selected);
        if (index < 0 || active != player) return;
        externalSubtitlesEnabled = false;
        externalSubtitle.IsVisible = false;
        if (index == 0)
        {
            active.SetSpu(-1);
            if (AppOptions.RememberTrackSelection) Preferences.Default.Set("rememberedSubtitleTrack", "__off__");
        }
        else
        {
            active.SetSpu(tracks[index - 1].Id);
            if (AppOptions.RememberTrackSelection)
                Preferences.Default.Set("rememberedSubtitleTrack", tracks[index - 1].Name ?? labels[index]);
        }
        ShowControlsTemporarily();
    }
    private Page? FindParentPage() { Element? p = this; while (p is not null && p is not Page) p = p.Parent; return p as Page; }
#endif
}
