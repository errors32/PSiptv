namespace PSiptv.Services;

public static class DeviceProfile
{
    public static bool IsAutomotive
    {
        get
        {
#if ANDROID
            return Android.App.Application.Context.PackageManager?
                .HasSystemFeature("android.hardware.type.automotive") == true;
#else
            return false;
#endif
        }
    }
}
