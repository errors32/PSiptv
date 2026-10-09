using System.Security.Cryptography;
using System.Text;
using PSiptv.Core;

namespace PSiptv.Services;

internal static class LogoCacheService
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim downloads = new(4);
    private static readonly SharedWork<string, string?> transfers = new();

    internal static Task<string?> LoadAsync(string url, CancellationToken token) =>
        transfers.RunAsync(url, () => DownloadAsync(url), token);

    private static async Task<string?> DownloadAsync(string url)
    {
        WebAddress.Require(url);
        var path = Path.Combine(FileSystem.CacheDirectory, "logos",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await downloads.WaitAsync(timeout.Token);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0 && File.GetLastWriteTimeUtc(path) >= DateTime.UtcNow.AddDays(-1))
                return path;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", AppOptions.UserAgent);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 4_000_000) return null;
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > 4_000_000) return null;
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            if (output.Length == 0) return null;
            await AtomicFile.WriteAsync(path, output.ToArray(), timeout.Token);
            return path;
        }
        finally { downloads.Release(); }
    }
}
