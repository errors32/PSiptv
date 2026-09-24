namespace PSiptv.Services;

/// <summary>Reads and changes the receiver's real media-output volume when the platform permits it.</summary>
public static class DeviceVolumeService
{
    public static int GetMediaVolume(int fallback)
    {
#if ANDROID
        var manager = Microsoft.Maui.ApplicationModel.Platform.AppContext
            .GetSystemService(Android.Content.Context.AudioService) as Android.Media.AudioManager;
        if (manager is not null)
        {
            var maximum = manager.GetStreamMaxVolume(Android.Media.Stream.Music);
            if (maximum > 0)
                return (int)Math.Round(manager.GetStreamVolume(Android.Media.Stream.Music) * 100d / maximum);
        }
#endif
        return Math.Clamp(fallback, 0, 100);
    }

    public static bool TrySetMediaVolume(int percentage)
    {
#if ANDROID
        var manager = Microsoft.Maui.ApplicationModel.Platform.AppContext
            .GetSystemService(Android.Content.Context.AudioService) as Android.Media.AudioManager;
        if (manager is null || manager.IsVolumeFixed) return false;
        var maximum = manager.GetStreamMaxVolume(Android.Media.Stream.Music);
        if (maximum <= 0) return false;
        var target = (int)Math.Round(Math.Clamp(percentage, 0, 100) * maximum / 100d);
        manager.SetStreamVolume(Android.Media.Stream.Music, target, (Android.Media.VolumeNotificationFlags)0);
        return true;
#else
        return false;
#endif
    }
}
