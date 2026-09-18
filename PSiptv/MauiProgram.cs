#if ANDROID
using LibVLCSharp.MAUI;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;
using PSiptv.Views;
#endif
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace PSiptv
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("FontAwesomeFreeSolid.otf", "FontAwesomeFreeSolid");
                    fonts.AddFont("FontAwesomeFreeBrands.otf", "FontAwesomeFreeBrands");
                });

#if ANDROID
            builder.UseLibVLCSharp();
            BorderHandler.Mapper.AppendToMapping("AndroidTvFocus", (handler, view) =>
            {
                if (view is not TvFocusableBorder card || !Ui.UsesLargeControls) return;
                handler.PlatformView.Focusable = true;
                handler.PlatformView.FocusableInTouchMode = false;
                handler.PlatformView.Clickable = true;
                if (Ui.IsTelevision) AndroidTvFocus.Attach(handler.PlatformView, card);
            });
            CollectionViewHandler.Mapper.AppendToMapping("AndroidTvCardNavigation", (handler, _) =>
            {
                if (!Ui.IsTelevision) return;

                // A RecyclerView is focusable by default. On Android TV that
                // makes the D-pad scroll the collection while focus remains on
                // the collection itself, so no card ever enters its focused
                // state. Only the item descendants should be focus targets.
                handler.PlatformView.Focusable = false;
                handler.PlatformView.FocusableInTouchMode = false;
                handler.PlatformView.DescendantFocusability = Android.Views.DescendantFocusability.AfterDescendants;
                AndroidTvFocus.AttachCollection(handler.PlatformView);
            });
#endif
#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}




