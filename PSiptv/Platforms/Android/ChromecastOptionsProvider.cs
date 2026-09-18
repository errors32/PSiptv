using Android.Content;
using Android.Gms.Cast;
using Android.Gms.Cast.Framework;
using Android.Runtime;

namespace PSiptv.Platforms.Android;

[Register("com.PS.PSiptv.ChromecastOptionsProvider")]
public sealed class ChromecastOptionsProvider : Java.Lang.Object, IOptionsProvider
{
    public CastOptions GetCastOptions(Context context) => new CastOptions.Builder()
        .SetReceiverApplicationId(CastMediaControlIntent.DefaultMediaReceiverApplicationId)
        .SetResumeSavedSession(true)
        .Build();

    public IList<SessionProvider>? GetAdditionalSessionProviders(Context context) => null;
}
