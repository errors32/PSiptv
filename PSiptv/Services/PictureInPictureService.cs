using PSiptv.Views;
namespace PSiptv.Services;

public static class PictureInPictureService
{
    private static long lastPhoneExit;
    private static bool phoneResumeObservedWhileActive;
    public static PlaybackView? Current { get; set; }
    public static bool IsActive { get; private set; }
    public static event Action? Changed;
    public static bool Supported => !DeviceProfile.IsAutomotive && DeviceInfo.Platform == DevicePlatform.Android && OperatingSystem.IsAndroidVersionAtLeast(26);
    public static void SetActive(bool active)
    {
        var wasActive = IsActive;
        if (wasActive == active) return;
        IsActive = active;
        if (DeviceInfo.Idiom == DeviceIdiom.Phone)
        {
            if (active)
            {
                lastPhoneExit = 0;
                phoneResumeObservedWhileActive = false;
            }
            else if (wasActive)
            {
                // Depending on the manufacturer, Window.Resumed can be raised
                // either before or after the PiP callback. Only leave a pending
                // resume signal when it has not already been observed.
                lastPhoneExit = phoneResumeObservedWhileActive ? 0 : Environment.TickCount64;
                phoneResumeObservedWhileActive = false;
            }
        }
        ScreenOrientationService.SetPictureInPicture(active);
        // Resize the MAUI layout before asking LibVLC to refresh its native
        // surface. Doing this in the opposite order refreshes stale bounds.
        Changed?.Invoke();
        Current?.HandlePictureInPictureChanged(active);
    }

    public static bool ShouldPreservePlaybackOnResume()
    {
        if (DeviceInfo.Idiom != DeviceIdiom.Phone) return false;
        if (IsActive)
        {
            phoneResumeObservedWhileActive = true;
            return true;
        }
        var recentExit = lastPhoneExit > 0 && Environment.TickCount64 - lastPhoneExit < 5000;
        lastPhoneExit = 0;
        return recentExit;
    }
    public static void Enter()
    {
#if ANDROID
        if (DeviceProfile.IsAutomotive || !AppOptions.Pip || Current?.IsPlaying != true || AppServices.ActiveAccount is null || !OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var activity = Platform.CurrentActivity;
        if (activity is null) return;
        using var builder = new Android.App.PictureInPictureParams.Builder();
        using var ratio = new Android.Util.Rational(16, 9);
        builder.SetAspectRatio(ratio);
        Current.ConfigurePictureInPicture(builder);
        using var parameters = builder.Build();
        try
        {
            // Some Android versions invoke OnPictureInPictureModeChanged during
            // this call and others immediately afterwards. SetActive is
            // idempotent, so either ordering now produces one transition only.
            if (activity.EnterPictureInPictureMode(parameters!) && !IsActive) SetActive(true);
        }
        catch (Java.Lang.Exception) { SetActive(false); }
#endif
    }
}
