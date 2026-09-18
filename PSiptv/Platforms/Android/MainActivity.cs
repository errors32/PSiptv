using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;

namespace PSiptv
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ResizeableActivity = true, SupportsPictureInPicture = true, LaunchMode = LaunchMode.SingleTask, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter([Intent.ActionMain], Categories = ["android.intent.category.LEANBACK_LAUNCHER"])]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            AndroidNotificationNavigation.Capture(Intent);
            if (OperatingSystem.IsAndroidVersionAtLeast(28) && Window?.Attributes is { } attributes)
            {
                // Allow fullscreen video to occupy the display-cutout strip.
                // Applying this at activity creation also covers OEMs that do
                // not reliably honour a later runtime-only change.
                attributes.LayoutInDisplayCutoutMode = OperatingSystem.IsAndroidVersionAtLeast(30)
                    ? Android.Views.LayoutInDisplayCutoutMode.Always
                    : Android.Views.LayoutInDisplayCutoutMode.ShortEdges;
                Window.Attributes = attributes;
            }
            if (PackageManager?.HasSystemFeature("android.software.leanback") == true)
                RequestedOrientation = ScreenOrientation.Landscape;
        }
        protected override void OnPause()
        {
            if (PSiptv.Services.DeviceProfile.IsAutomotive) PSiptv.Services.AppServices.SuspendPlayback();
            base.OnPause();
        }
        protected override void OnResume()
        {
            base.OnResume();
            PSiptv.Services.AndroidAppUpdateService.TryResumePendingInstall(this);
        }
        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            AndroidNotificationNavigation.Capture(intent);
        }
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            AndroidFolderGrant.HandleResult(this, requestCode, resultCode, data);
            base.OnActivityResult(requestCode, resultCode, data);
        }
        public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            Window?.DecorView.Post(PSiptv.Services.ScreenOrientationService.ReapplySystemBars);
        }
        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            if (hasFocus) Window?.DecorView.Post(PSiptv.Services.ScreenOrientationService.ReapplySystemBars);
        }
        protected override void OnUserLeaveHint() { PSiptv.Services.PictureInPictureService.Enter(); base.OnUserLeaveHint(); }
        public override void OnPictureInPictureModeChanged(bool active, Android.Content.Res.Configuration? configuration) { base.OnPictureInPictureModeChanged(active, configuration); PSiptv.Services.PictureInPictureService.SetActive(active); }
    }

    internal static class AndroidFolderGrant
    {
        private const int RequestCode = 0x5053;
        private static TaskCompletionSource<string?>? pending;
        internal static string? LatestTreeUri { get; set; }

        internal static async Task<string?> PickAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref pending, completion, null) is not null)
                throw new InvalidOperationException("Já existe um seletor de pastas aberto.");
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var activity = Platform.CurrentActivity as MainActivity
                        ?? throw new InvalidOperationException("Não foi possível abrir o seletor de pastas.");
                    using var intent = new Intent(Intent.ActionOpenDocumentTree);
                    intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                                    ActivityFlags.GrantWriteUriPermission |
                                    ActivityFlags.GrantPersistableUriPermission |
                                    ActivityFlags.GrantPrefixUriPermission);
                    activity.StartActivityForResult(intent, RequestCode);
                });
                return await completion.Task;
            }
            finally { Interlocked.CompareExchange(ref pending, null, completion); }
        }

        internal static Task<bool> OpenAsync(string treeUriText)
        {
            if (string.IsNullOrWhiteSpace(treeUriText)) return Task.FromResult(false);
            return MainThread.InvokeOnMainThreadAsync(() =>
            {
                var activity = Platform.CurrentActivity as MainActivity;
                var treeUri = Android.Net.Uri.Parse(treeUriText);
                if (activity is null || treeUri is null || !DocumentsContract.IsTreeUri(treeUri)) return false;

                var documentId = DocumentsContract.GetTreeDocumentId(treeUri);
                var documentUri = string.IsNullOrWhiteSpace(documentId)
                    ? treeUri : DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId);
                using var view = new Intent(Intent.ActionView);
                view.SetDataAndType(documentUri, DocumentsContract.Document.MimeTypeDir);
                view.AddCategory(Intent.CategoryDefault);
                view.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
                if (view.ResolveActivity(activity.PackageManager!) is not null)
                {
                    activity.StartActivity(view);
                    return true;
                }

                // Some Android variants do not expose a direct directory viewer.
                // Their system document picker is still a proper file browser and
                // can be opened at the granted folder without changing the grant.
                using var browse = new Intent(Intent.ActionOpenDocumentTree);
                browse.PutExtra(DocumentsContract.ExtraInitialUri, documentUri);
                browse.AddFlags(ActivityFlags.GrantReadUriPermission |
                                ActivityFlags.GrantWriteUriPermission |
                                ActivityFlags.GrantPersistableUriPermission |
                                ActivityFlags.GrantPrefixUriPermission);
                if (browse.ResolveActivity(activity.PackageManager!) is null) return false;
                activity.StartActivity(browse);
                return true;
            });
        }

        internal static bool HandleResult(MainActivity activity, int requestCode, Result resultCode, Intent? data)
        {
            if (requestCode != RequestCode) return false;
            var completion = pending;
            if (completion is null) return true;
            if (resultCode != Result.Ok || data?.Data is not { } uri)
            {
                completion.TrySetResult(null);
                return true;
            }
            try
            {
                var grants = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
                if ((grants & ActivityFlags.GrantWriteUriPermission) == 0)
                    throw new UnauthorizedAccessException("O Android não concedeu acesso de escrita à pasta.");
                var resolver = activity.ContentResolver
                    ?? throw new InvalidOperationException("Não foi possível aceder ao armazenamento do Android.");
                resolver.TakePersistableUriPermission(uri, grants);
                if (!(resolver.PersistedUriPermissions ?? []).Any(permission =>
                        permission.IsWritePermission && permission.Uri?.ToString() == uri.ToString()))
                    throw new UnauthorizedAccessException("O Android não conservou o acesso de escrita à pasta.");
                LatestTreeUri = uri.ToString();
                completion.TrySetResult(DisplayPath(activity, uri));
            }
            catch (Java.Lang.SecurityException)
            {
                completion.TrySetException(new UnauthorizedAccessException(
                    "Não foi possível conservar a autorização da pasta. Escolha outra pasta e tente novamente."));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            return true;
        }

        private static string DisplayPath(MainActivity activity, Android.Net.Uri uri)
        {
            var documentId = DocumentsContract.GetTreeDocumentId(uri) ?? "";
            var separator = documentId.IndexOf(':');
            if (separator >= 0)
            {
                var volume = documentId[..separator];
                var relative = documentId[(separator + 1)..].Replace('\\', '/').Trim('/');
                var root = volume.Equals("primary", StringComparison.OrdinalIgnoreCase)
                    ? "/storage/emulated/0" : "/storage/" + volume;
                return root + (relative.Length == 0 ? "" : "/" + relative);
            }
            var tree = AndroidX.DocumentFile.Provider.DocumentFile.FromTreeUri(activity, uri);
            return "/" + (tree?.Name ?? "Pasta selecionada");
        }
    }

    internal static class AndroidNotificationNavigation
    {
        private static string? pendingDestination;

        internal static void Capture(Intent? intent)
        {
            var destination = intent?.GetStringExtra("psiptv.destination");
            if (!string.IsNullOrWhiteSpace(destination))
                Interlocked.Exchange(ref pendingDestination, destination);
        }

        internal static string? Consume() => Interlocked.Exchange(ref pendingDestination, null);

        internal static PendingIntent? Create(Context context, string destination, int requestCode)
        {
            var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
            if (open is null) return null;
            open.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            open.PutExtra("psiptv.destination", destination);
            return PendingIntent.GetActivity(context, requestCode, open,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }
    }

    internal static class AndroidNotificationPermission
    {
        internal static async Task RequestAsync()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
            if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
    }
}

