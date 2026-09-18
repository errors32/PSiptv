using System.Security.Cryptography;
using System.Text;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class LogoImage : Image
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim downloads = new(4);
    private CancellationTokenSource? loading;
    public LogoImage() { Unloaded += (_, _) => loading?.Cancel(); Loaded += (_, _) => Load(); }
    protected override void OnBindingContextChanged() { base.OnBindingContextChanged(); Load(); }
    private async void Load()
    {
        loading?.Cancel(); Source = null;
        if (BindingContext is not MediaItem item || item.Logo.Length == 0) return;
        var cancel = new CancellationTokenSource(); loading = cancel;
        try
        {
            WebAddress.Require(item.Logo);
            var folder = Path.Combine(FileSystem.CacheDirectory, "logos");
            var path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Logo))) + ".img");
            await downloads.WaitAsync(cancel.Token);
            try
            {
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, item.Logo);
                    request.Headers.TryAddWithoutValidation("User-Agent", AppOptions.UserAgent);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 4_000_000) return;
                    await using var input = await response.Content.ReadAsStreamAsync(cancel.Token);
                    using var output = new MemoryStream(); var buffer = new byte[16384]; int count;
                    while ((count = await input.ReadAsync(buffer, cancel.Token)) > 0) { if (output.Length + count > 4_000_000) return; output.Write(buffer, 0, count); }
                    Directory.CreateDirectory(folder);
                    await File.WriteAllBytesAsync(path, output.ToArray(), cancel.Token);
                }
            }
            finally { downloads.Release(); }
            if (!cancel.IsCancellationRequested) Source = ImageSource.FromFile(path);
        }
        catch (Exception) { /* Missing artwork must not stop browsing. */ }
        finally { if (loading == cancel) loading = null; cancel.Dispose(); }
    }
}
