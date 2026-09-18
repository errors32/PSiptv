namespace PSiptv.Services;

public static class ScreenOrientationService
{
#if ANDROID
    private static Android.Content.PM.ScreenOrientation? previous;
    private static bool fullscreenActive;
#endif
    public static void SetPictureInPicture(bool active)
    {
#if ANDROID
        ReapplySystemBars();
#endif
    }
#if ANDROID
    private static void SetSystemBars(bool hidden)
    {
        var window = Platform.CurrentActivity?.Window;
        if (window is null) return;
        AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, !hidden);
        if (hidden)
        {
            window.AddFlags(Android.Views.WindowManagerFlags.LayoutNoLimits |
                Android.Views.WindowManagerFlags.Fullscreen);
            window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
            // Keep the legacy flags as an OEM fallback. Some Xiaomi/HyperOS
            // versions leave the status/cutout strip reserved in landscape
            // even after the modern insets controller hides the bars.
            window.DecorView.SystemUiFlags =
                Android.Views.SystemUiFlags.ImmersiveSticky |
                Android.Views.SystemUiFlags.Fullscreen |
                Android.Views.SystemUiFlags.HideNavigation |
                Android.Views.SystemUiFlags.LayoutFullscreen |
                Android.Views.SystemUiFlags.LayoutHideNavigation |
                Android.Views.SystemUiFlags.LayoutStable;
        }
        else
        {
            window.ClearFlags(Android.Views.WindowManagerFlags.LayoutNoLimits |
                Android.Views.WindowManagerFlags.Fullscreen);
            window.DecorView.SystemUiFlags = Android.Views.SystemUiFlags.Visible;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(28) && window.Attributes is { } attributes)
        {
            attributes.LayoutInDisplayCutoutMode = hidden
                ? OperatingSystem.IsAndroidVersionAtLeast(30)
                    ? Android.Views.LayoutInDisplayCutoutMode.Always
                    : Android.Views.LayoutInDisplayCutoutMode.ShortEdges
                : Android.Views.LayoutInDisplayCutoutMode.Default;
            window.Attributes = attributes;
        }
        var controls = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
        if (controls is null) return;
        if (hidden)
        {
            controls.SystemBarsBehavior = AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            controls.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
        }
        else controls.Show(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
        AndroidX.Core.View.ViewCompat.RequestApplyInsets(window.DecorView);
    }
#endif
    public static void ReapplySystemBars()
    {
#if ANDROID
        SetSystemBars(fullscreenActive || PictureInPictureService.IsActive);
#endif
    }
    public static void SetFullscreen(bool fullscreen)
    {
#if ANDROID
        if (DeviceProfile.IsAutomotive) return;
        var activity = Platform.CurrentActivity;
        if (activity is null) return;
        fullscreenActive = fullscreen;
        ReapplySystemBars();
        if (fullscreen)
        {
            previous ??= activity.RequestedOrientation;
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.SensorLandscape;
        }
        else if (previous is { } orientation)
        {
            activity.RequestedOrientation = orientation;
            previous = null;
        }
#endif
    }
}


