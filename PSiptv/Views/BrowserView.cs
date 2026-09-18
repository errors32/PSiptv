using System.Text.Json;
using PSiptv.Core;
using PSiptv.Services;
#if ANDROID
using Android.Webkit;
#endif

namespace PSiptv.Views;

public sealed class BrowserView : ContentView
{
    private readonly Microsoft.Maui.Controls.WebView web = new();
    private readonly Picker sources = new() { Title = "Escolher fonte" };
    private readonly Entry address = new()
    {
        Placeholder = "https://exemplo.pt",
        Keyboard = Keyboard.Url,
        ReturnType = ReturnType.Go,
        ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        FontSize = 13,
        HorizontalOptions = LayoutOptions.Fill
    };
    private readonly Button playStream;
    private readonly IDispatcherTimer streamDetector;
    private string detectedStream = "";
    private int detectedPriority;
    private string detectedReferer = "";
    private string detectedUserAgent = "";
    private string detectedCookie = "";
    private bool detectingStream;
    private bool refreshing;
#if ANDROID
    private bool blockerAttached;
#endif

    public BrowserView()
    {
        sources.SetDynamicResource(Picker.TextColorProperty, "Ink");
        var back = BrowserButton(FaIcons.ChevronLeft, () => { GoBack(); return Task.CompletedTask; }, "Voltar");
        var forward = BrowserButton(FaIcons.ChevronRight, () => { GoForward(); return Task.CompletedTask; }, "Avançar");
        var reload = BrowserButton(FaIcons.ArrowsRotate, () => { Reload(); return Task.CompletedTask; }, "Atualizar página");
        var go = BrowserButton(FaIcons.Globe, NavigateAddressAsync, "Abrir endereço");
        var external = BrowserButton(FaIcons.ArrowUpRightFromSquare, OpenExternalAsync, "Abrir num browser externo");
        playStream = BrowserButton(FaIcons.CirclePlay, OpenStreamAsync, "Reproduzir stream no leitor");
        playStream.IsEnabled = false;
        SemanticProperties.SetDescription(external, LanguageService.Text("Abrir num browser externo"));
        ToolTipProperties.SetText(external, "Abrir num browser externo");
        var shield = Ui.FontIcon(FaIcons.Shield, 20);
        SemanticProperties.SetDescription(shield, LanguageService.Text("Bloqueador de publicidade e popups ativo"));
        var toolbar = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)]
        };
        toolbar.Add(back); toolbar.Add(forward, 1); toolbar.Add(sources, 2); toolbar.Add(playStream, 3);
        toolbar.Add(external, 4); toolbar.Add(shield, 5); toolbar.Add(reload, 6);
        var grid = new Grid
        {
            RowSpacing = 4,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)]
        };
        var addressBar = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        addressBar.Add(address);
        addressBar.Add(go, 1);
        // Saved sources are the remote-friendly entry point on Android TV.
        // Keep free-form URL entry available on touch/keyboard devices without
        // spending a full row of the television viewport on it.
        addressBar.IsVisible = !Ui.IsTelevision;
        grid.Add(toolbar); grid.Add(addressBar, 0, 1); grid.Add(web, 0, 2);
        Content = grid;
        address.SetDynamicResource(Entry.TextColorProperty, "Ink");
        address.SetDynamicResource(Entry.PlaceholderColorProperty, "Muted");
        address.Completed += (_, _) => _ = NavigateAddressAsync();
        sources.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing || sources.SelectedIndex < 0) return;
            var saved = BrowserSourcesService.Sources;
            if (sources.SelectedIndex < saved.Count) Open(saved[sources.SelectedIndex]);
        };
        web.Navigating += (_, e) =>
        {
            address.Text = e.Url;
            ResetStreamDetection();
            ObserveStream(e.Url);
        };
        web.Navigated += (_, e) =>
        {
            address.Text = e.Url;
            ObserveStream(e.Url);
            _ = DetectStreamAsync();
        };
#if ANDROID
        web.HandlerChanged += (_, _) => AttachAndroidBlocker();
#endif
        BrowserSourcesService.Changed += RefreshSources;
        streamDetector = Dispatcher.CreateTimer();
        streamDetector.Interval = TimeSpan.FromSeconds(2);
        streamDetector.Tick += async (_, _) =>
        {
            if (IsVisible) await DetectStreamAsync();
        };
        Loaded += (_, _) => streamDetector.Start();
        Unloaded += (_, _) => streamDetector.Stop();
        ResetStreamDetection();
        RotateSources();
    }

    private static Button BrowserButton(string glyph, Func<Task> action, string description)
    {
        var button = Ui.Button("", action);
        button.ImageSource = Ui.FontIconSource(glyph, 20);
        button.WidthRequest = 46;
        button.Padding = 9;
        SemanticProperties.SetDescription(button, LanguageService.Text(description));
        ToolTipProperties.SetText(button, LanguageService.Text(description));
        return button;
    }

    public void Activate()
    {
        RefreshSources();
        if (BrowserSourcesService.Default is { } source) Open(source);
    }

    private void RefreshSources() => Dispatcher.Dispatch(RotateSources);

    private void RotateSources()
    {
        refreshing = true;
        var saved = BrowserSourcesService.Sources;
        sources.ItemsSource = saved.Select(source => source.Name).ToArray();
        var current = BrowserSourcesService.Default;
        sources.SelectedIndex = current is null ? -1 : saved.ToList().FindIndex(source => source.Id == current.Id);
        refreshing = false;
        if (saved.Count == 0)
        {
            ResetStreamDetection();
            address.Text = "Adicione uma fonte nas configurações.";
            web.Source = new HtmlWebViewSource { Html = $"<html><body style='background:#0f172c;color:white;font-family:sans-serif;padding:28px'><h2>{LanguageService.Text("Sem fontes")}</h2><p>{LanguageService.Text("Adicione um endereço nas configurações da aplicação.")}</p></body></html>" };
        }
    }

    private void Open(BrowserSource source)
    {
        address.Text = source.Url;
        web.Source = source.Url;
    }

    private async Task NavigateAddressAsync()
    {
        var value = address.Text?.Trim() ?? "";
        if (value.Length == 0) return;
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        try
        {
            var uri = WebAddress.Require(value);
            address.Text = uri.AbsoluteUri;
            ResetStreamDetection();
            web.Source = uri.AbsoluteUri;
            address.Unfocus();
        }
        catch (InvalidOperationException)
        {
            var page = FindPage();
            if (page is not null)
                await LanguageService.AlertAsync(page, "Endereço inválido", "Introduza um endereço HTTP ou HTTPS válido.");
        }
    }

    private void GoBack()
    {
#if ANDROID
        if (web.Handler?.PlatformView is Android.Webkit.WebView native && native.CanGoBack()) { native.GoBack(); return; }
#endif
        if (web.CanGoBack) web.GoBack();
    }

    private void GoForward()
    {
#if ANDROID
        if (web.Handler?.PlatformView is Android.Webkit.WebView native && native.CanGoForward()) { native.GoForward(); return; }
#endif
        if (web.CanGoForward) web.GoForward();
    }

    private void Reload()
    {
#if ANDROID
        if (web.Handler?.PlatformView is Android.Webkit.WebView native) { native.Reload(); return; }
#endif
        web.Reload();
    }

    private async Task OpenExternalAsync()
    {
        var url = address.Text;
#if ANDROID
        if (web.Handler?.PlatformView is Android.Webkit.WebView native && !string.IsNullOrWhiteSpace(native.Url))
            url = native.Url;
#endif
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return;

        try
        {
#if ANDROID
            var activity = Platform.CurrentActivity;
            if (activity is null) return;
            var packageManager = activity.PackageManager;
            if (packageManager is null) return;

            var resolved = new List<Android.Content.PM.ResolveInfo>();
            void AddMatches(Android.Content.Intent query)
            {
#pragma warning disable CA1422 // The compatibility overload is required below Android 13.
                resolved.AddRange(packageManager.QueryIntentActivities(query, (Android.Content.PM.PackageInfoFlags)0));
#pragma warning restore CA1422
            }

            foreach (var scheme in new[] { "https", "http" })
            {
                var webIntent = new Android.Content.Intent(Android.Content.Intent.ActionView,
                    Android.Net.Uri.Parse($"{scheme}://www.example.com/"));
                webIntent.AddCategory(Android.Content.Intent.CategoryBrowsable);
                AddMatches(webIntent);
            }
            var browserSelector = Android.Content.Intent.MakeMainSelectorActivity(
                Android.Content.Intent.ActionMain, Android.Content.Intent.CategoryAppBrowser);
            if (browserSelector is not null) AddMatches(browserSelector);

            resolved = resolved
                .Where(result => result.ActivityInfo?.PackageName is not null)
                .GroupBy(result => result.ActivityInfo!.PackageName!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var page = FindPage();
            if (page is null) return;
            if (resolved.Count == 0)
            {
                await LanguageService.AlertAsync(page, "Abrir com", "Não foi encontrado nenhum browser instalado.");
                return;
            }

            var labels = resolved.Select(result => result.LoadLabel(packageManager)?.ToString()
                    ?? result.ActivityInfo!.PackageName!)
                .ToArray();
            for (var index = 0; index < labels.Length; index++)
            {
                if (labels.Count(label => string.Equals(label, labels[index], StringComparison.OrdinalIgnoreCase)) > 1)
                    labels[index] = $"{labels[index]} ({resolved[index].ActivityInfo!.PackageName})";
            }

            var selected = await LanguageService.ActionSheetAsync(page, "Abrir com…", "Cancelar", null, labels);
            var selectedIndex = Array.IndexOf(labels, selected);
            if (selectedIndex < 0 || resolved[selectedIndex].ActivityInfo?.PackageName is not { } browserPackage) return;

            var launch = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(uri.AbsoluteUri));
            launch.AddCategory(Android.Content.Intent.CategoryBrowsable);
            // A browser's launcher Activity is often different from its URL
            // handler, so target the selected package and let it resolve that
            // internal detail itself.
            launch.SetPackage(browserPackage);
            activity.StartActivity(launch);
#else
            await Launcher.Default.OpenAsync(uri);
#endif
        }
        catch (Exception ex)
        {
            await Ui.ErrorAsync(this, ex);
        }
    }

    private async Task OpenStreamAsync()
    {
        var url = detectedStream;
        if (!BrowserStreamDetection.IsLikelyStream(url) &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var detected) || detected.Scheme is not ("http" or "https")))
            return;
        var page = FindPage();
        if (page is null) return;
        var uri = WebAddress.Require(url);
        var sourceName = sources.SelectedIndex >= 0 && sources.SelectedIndex < BrowserSourcesService.Sources.Count
            ? BrowserSourcesService.Sources[sources.SelectedIndex].Name
            : uri.Host;
#if ANDROID
        if (web.Handler?.PlatformView is Android.Webkit.WebView native)
        {
            detectedUserAgent = FirstValue(detectedUserAgent, native.Settings.UserAgentString);
            detectedCookie = FirstValue(CookieManager.Instance?.GetCookie(uri.AbsoluteUri), detectedCookie);
        }
#endif
        var referer = FirstValue(detectedReferer, CurrentPageUrl());
        var item = new MediaItem($"browser-{Guid.NewGuid():N}", sourceName, "Browser",
            MediaKind.Movie, uri.AbsoluteUri, IsCatchup: true,
            HttpReferer: referer, HttpUserAgent: detectedUserAgent, HttpCookie: detectedCookie);
        await page.Navigation.PushAsync(new PlayerPage(item, transient: true));
    }

    private string CurrentPageUrl()
    {
#if ANDROID
        if (web.Handler?.PlatformView is Android.Webkit.WebView native &&
            Uri.TryCreate(native.Url, UriKind.Absolute, out var nativeUri) &&
            nativeUri.Scheme is "http" or "https")
            return nativeUri.AbsoluteUri;
#endif
        return Uri.TryCreate(address.Text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : "";
    }

    private static string FirstValue(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback?.Trim() ?? "";

    private void ResetStreamDetection()
    {
        detectedStream = "";
        detectedPriority = 0;
        detectedReferer = "";
        detectedUserAgent = "";
        detectedCookie = "";
        playStream.IsEnabled = false;
        playStream.Opacity = 0.4;
    }

    private void ObserveStream(string? value, bool trustedMediaElement = false,
        string? referer = null, string? userAgent = null, string? cookie = null)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https")) return;
        var priority = BrowserStreamDetection.Priority(uri.AbsoluteUri);
        if (priority == 0 && !trustedMediaElement) return;
        priority = Math.Max(priority, 1);
        Dispatcher.Dispatch(() =>
        {
            if (priority < detectedPriority) return;
            detectedStream = uri.AbsoluteUri;
            detectedPriority = priority;
            detectedReferer = FirstValue(referer, detectedReferer);
            detectedUserAgent = FirstValue(userAgent, detectedUserAgent);
            detectedCookie = FirstValue(cookie, detectedCookie);
            playStream.IsEnabled = true;
            playStream.Opacity = 1;
        });
    }

    private async Task DetectStreamAsync()
    {
        if (detectingStream || web.Source is HtmlWebViewSource) return;
        detectingStream = true;
        try
        {
            var result = await web.EvaluateJavaScriptAsync(StreamDetectionScript);
            var candidate = DecodeJavaScriptString(result);
            if (candidate.Length > 0) ObserveStream(candidate, trustedMediaElement: true);
        }
        catch (Exception) { /* Navigation can replace the JavaScript context while it is being inspected. */ }
        finally { detectingStream = false; }
    }

    private static string DecodeJavaScriptString(string? value)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result is "null" or "undefined") return "";
        if (result[0] != '"') return result;
        try { return JsonSerializer.Deserialize<string>(result) ?? ""; }
        catch (JsonException) { return result.Trim('"'); }
    }

    private Page? FindPage()
    {
        Element? parent = this;
        while (parent is not null && parent is not Page) parent = parent.Parent;
        return parent as Page;
    }

#if ANDROID
    private void AttachAndroidBlocker()
    {
        if (blockerAttached || web.Handler?.PlatformView is not Android.Webkit.WebView native) return;
        blockerAttached = true;
        native.Settings.SetSupportMultipleWindows(false);
        native.Settings.JavaScriptCanOpenWindowsAutomatically = false;
        CookieManager.Instance?.SetAcceptThirdPartyCookies(native, false);
        native.SetWebChromeClient(new PopupBlockingChromeClient());
        native.SetWebViewClient(new BlockingWebViewClient(this));
    }

    private void PageStarted(Android.Webkit.WebView view, string? url)
    {
        Dispatcher.Dispatch(() =>
        {
            address.Text = url ?? "";
            ResetStreamDetection();
            ObserveStream(url);
        });
        view.EvaluateJavascript(AdHidingScript, null);
    }

    private void PageFinished(Android.Webkit.WebView view, string? url)
    {
        Dispatcher.Dispatch(() =>
        {
            address.Text = url ?? "";
            ObserveStream(url);
            _ = DetectStreamAsync();
        });
        view.EvaluateJavascript(AdHidingScript, null);
    }

    private static bool IsBlocked(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        return BlockedHosts.Any(blocked => host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlockedResource(string? value)
    {
        if (IsBlocked(value)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;

        var labels = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Any(label => BlockedHostLabels.Contains(label))) return true;

        var path = uri.AbsolutePath.ToLowerInvariant();
        return BlockedPathFragments.Any(path.Contains);
    }

    private static string? RequestHeader(IDictionary<string, string>? headers, string name)
    {
        if (headers is null) return null;
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                return header.Value;
        }
        return null;
    }

    private static readonly HashSet<string> BlockedHostLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "ad", "ads", "adserver", "adservers", "adservice", "adservices", "adnetwork",
        "advert", "advertising", "analytics", "telemetry", "tracking", "tracker", "trackers",
        "popads", "popunder", "sponsor", "sponsors"
    };

    private static readonly string[] BlockedPathFragments =
    [
        "/ads/", "/ad/", "/advert/", "/adverts/", "/advertising/", "/adserver/", "/adserve/",
        "/banner-ad", "/banner_ads", "/popunder", "/popup-ad", "/prebid", "/pagead/",
        "/vast/", "/vpaid/", "/outstream/", "/instream/", "adsbygoogle", "googletag",
        "google-ima", "ima3.js", "pubads_", "adservice.", "adserver."
    ];

    private static readonly string[] BlockedHosts =
    [
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "amazon-adsystem.com",
        "adnxs.com", "adsrvr.org", "taboola.com", "outbrain.com", "popads.net", "popcash.net",
        "propellerads.com", "adsterra.com", "exoclick.com", "trafficjunky.net", "mgid.com",
        "revcontent.com", "scorecardresearch.com", "quantserve.com", "criteo.com", "criteo.net",
        "2mdn.net", "adcolony.com", "adform.net", "adition.com", "admanmedia.com", "admob.com",
        "adpushup.com", "adroll.com", "adrta.com", "adsafeprotected.com", "adskeeper.com",
        "adscale.de", "adtech.de", "advertising.com", "amazon-adsystem.com", "amplitude.com",
        "appnexus.com", "bidswitch.net", "bluekai.com", "casalemedia.com", "chartbeat.com",
        "connatix.com", "contextweb.com", "demdex.net", "dianomi.com", "e-planning.net",
        "everesttech.net", "exponential.com", "flashtalking.com", "freewheel.tv", "gemius.pl",
        "google-analytics.com", "googletagmanager.com", "googletagservices.com", "hotjar.com",
        "imrworldwide.com", "indexww.com", "lijit.com", "mathtag.com", "moatads.com",
        "nativo.com", "newrelic.com", "omnitagjs.com", "onesignal.com", "openx.net",
        "optimizely.com", "outbrainimg.com", "parsely.com", "perfectaudience.com", "pubmatic.com",
        "quantcount.com", "rubiconproject.com", "segment.com", "sharethrough.com", "smartadserver.com",
        "smaato.net", "spotxchange.com", "statcounter.com", "stickyadstv.com", "teads.tv",
        "tradedoubler.com", "triplelift.com", "undertone.com", "yieldmo.com", "zedo.com"
    ];

    private const string AdHidingScript = """
        (() => {
          if (window.__psiptvAdBlockInstalled) {
            window.__psiptvAdBlockClean?.();
            return;
          }
          window.__psiptvAdBlockInstalled = true;
          const blockedOpen = () => null;
          try { window.open = blockedOpen; } catch (_) {}

          const selectors = [
            '[id="ad"]', '[id^="ad-"]', '[id^="ad_"]', '[id*="-ad-"]', '[id*="_ad_"]',
            '[id^="ads-"]', '[id^="ads_"]', '[id*="advert"]', '[id*="sponsor"]',
            '[class="ad"]', '[class^="ad-"]', '[class^="ad_"]', '[class*=" ad-"]',
            '[class*=" ad_"]', '[class*=" ads-"]', '[class*=" ads_"]', '[class*="advert"]',
            '[class*="banner-ad"]', '[class*="banner_ad"]', '[class*="commercial"]',
            '[class*="popunder"]', '[class*="sponsor"]', '.adsbox', '.adbox', '.ad-slot',
            '.ad-container', '.ad-wrapper', '.ad-banner', '.advertisement', '.google-auto-placed',
            '[aria-label*="advertisement" i]', '[aria-label*="publicidade" i]',
            '[data-ad]', '[data-ad-id]', '[data-ad-slot]', '[data-google-query-id]',
            'ins.adsbygoogle', 'amp-ad', 'amp-auto-ads', 'video-ads',
            'iframe[id^="google_ads"]', 'iframe[name^="google_ads"]',
            'iframe[src*="doubleclick"]', 'iframe[src*="googlesyndication"]',
            'iframe[src*="adservice"]', 'iframe[src*="adserver"]', 'iframe[src*="popads"]',
            'iframe[src*="exoclick"]', 'iframe[src*="trafficjunky"]'
          ];
          const css = `${selectors.join(',')} { display:none !important; visibility:hidden !important; ` +
                      `width:0 !important; height:0 !important; min-height:0 !important; margin:0 !important; padding:0 !important; }`;
          let scheduled = false;
          const clean = () => {
            scheduled = false;
            try {
              document.querySelectorAll(selectors.join(',')).forEach(node => {
                node.style.setProperty('display', 'none', 'important');
                node.setAttribute('aria-hidden', 'true');
              });
              document.documentElement?.style.setProperty('overflow', 'auto', 'important');
              document.body?.style.setProperty('overflow', 'auto', 'important');
            } catch (_) {}
          };
          const schedule = () => {
            if (scheduled) return;
            scheduled = true;
            setTimeout(clean, 60);
          };
          const install = () => {
            const root = document.documentElement;
            if (!root) { setTimeout(install, 25); return; }
            if (!document.getElementById('__psiptv_adblock_css')) {
              const style = document.createElement('style');
              style.id = '__psiptv_adblock_css';
              style.textContent = css;
              (document.head || root).appendChild(style);
            }
            clean();
            new MutationObserver(schedule).observe(root, {
              childList: true, subtree: true, attributes: true,
              attributeFilter: ['id', 'class', 'style', 'src']
            });
            setInterval(clean, 1500);
          };
          window.__psiptvAdBlockClean = clean;
          install();
        })();
        """;

    private sealed class PopupBlockingChromeClient : WebChromeClient
    {
        public override bool OnCreateWindow(Android.Webkit.WebView? view, bool isDialog, bool isUserGesture, Android.OS.Message? resultMsg) => false;
    }

    private sealed class BlockingWebViewClient(BrowserView owner) : WebViewClient
    {
        public override void OnPageStarted(Android.Webkit.WebView? view, string? url, Android.Graphics.Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            if (view is not null) owner.PageStarted(view, url);
        }

        public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url?.ToString();
            if (IsBlocked(url)) return true;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is not ("http" or "https" or "about" or "data")) return true;
            return false;
        }

        public override WebResourceResponse? ShouldInterceptRequest(Android.Webkit.WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url?.ToString();
            if (BrowserStreamDetection.IsLikelyStream(url))
            {
                var headers = request?.RequestHeaders;
                var referer = RequestHeader(headers, "Referer");
                var userAgent = RequestHeader(headers, "User-Agent");
                var cookie = FirstValue(RequestHeader(headers, "Cookie"),
                    url is null ? null : CookieManager.Instance?.GetCookie(url));
                owner.ObserveStream(url, referer: referer, userAgent: userAgent, cookie: cookie);
            }
            if (IsBlocked(url) || (request?.IsForMainFrame != true && IsBlockedResource(url)))
                return new WebResourceResponse("text/plain", "utf-8", new MemoryStream());
            return base.ShouldInterceptRequest(view, request);
        }

        public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            if (view is not null) owner.PageFinished(view, url);
        }
    }
#endif

    private const string StreamDetectionScript = """
        (() => {
          const mediaPattern = /(?:\.m3u8|\.mpd|\.mp4|\.m4v|\.webm|\.mov|\.mkv|\.avi|\.flv)(?:$|[?#])|(?:format|type)=(?:m3u8|mpd)(?:&|$)/i;
          const normalize = value => {
            try {
              const url = new URL(value, document.baseURI);
              return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : '';
            } catch (_) { return ''; }
          };
          if (!window.__psiptvStreamObserver) {
            window.__psiptvStreamObserver = true;
            window.__psiptvStreams = [];
            const remember = value => {
              const url = normalize(value);
              if (url && mediaPattern.test(url) && !window.__psiptvStreams.includes(url))
                window.__psiptvStreams.push(url);
            };
            try {
              const originalFetch = window.fetch;
              window.fetch = function(input, init) {
                remember(typeof input === 'string' ? input : input?.url);
                return originalFetch.apply(this, arguments);
              };
            } catch (_) {}
            try {
              const originalOpen = XMLHttpRequest.prototype.open;
              XMLHttpRequest.prototype.open = function(method, url) {
                remember(url);
                return originalOpen.apply(this, arguments);
              };
            } catch (_) {}
            try {
              new PerformanceObserver(list => list.getEntries().forEach(entry => remember(entry.name)))
                .observe({ type: 'resource', buffered: true });
            } catch (_) {}
          }

          const candidates = [];
          const add = (value, score) => {
            const url = normalize(value);
            if (url) candidates.push({ url, score });
          };
          document.querySelectorAll('video').forEach(media => {
            add(media.currentSrc, 90);
            add(media.src, 85);
          });
          document.querySelectorAll('video source').forEach(source => add(source.src, 85));
          document.querySelectorAll('a[href]').forEach(link => {
            if (mediaPattern.test(link.href)) add(link.href, 60);
          });
          try {
            performance.getEntriesByType('resource').forEach(entry => {
              if (mediaPattern.test(entry.name)) add(entry.name, /\.m3u8(?:$|[?#])/i.test(entry.name) ? 100 : 70);
            });
          } catch (_) {}
          (window.__psiptvStreams || []).forEach(url => add(url, /\.m3u8(?:$|[?#])/i.test(url) ? 100 : 70));
          candidates.sort((left, right) => right.score - left.score);
          return candidates[0]?.url || '';
        })()
        """;
}
