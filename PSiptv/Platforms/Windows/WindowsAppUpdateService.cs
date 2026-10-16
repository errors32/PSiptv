using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

public static class WindowsAppUpdateService
{
    private static readonly HttpClient http = CreateClient();
    private static int started;
    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    private static string Metadata(string key) => typeof(WindowsAppUpdateService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;
    public static string Repository => $"{Metadata("GitHubReleaseOwner")}/{Metadata("GitHubReleaseRepository")}";
    public static bool AutomaticChecksEnabled
    {
        get => Preferences.Default.Get("windows-updates.auto", true);
        set => Preferences.Default.Set("windows-updates.auto", value);
    }
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PSiptv-Windows-Updater/1.0");
        return client;
    }
    private static async Task<HttpRequestMessage> RequestAsync(string url, bool download)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = await GitHubUpdateTokenStore.ReadAsync();
        if (token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd(download ? "application/octet-stream" : "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode is System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            ? "Não foi possível aceder às Releases. Num repositório privado, configure um token GitHub com acesso de leitura a Contents."
            : $"O GitHub devolveu HTTP {(int)response.StatusCode}. Tente novamente mais tarde.");
    }
    public static async Task<WindowsRelease?> CheckLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var releases = new List<WindowsRelease>();
        // Inspect multiple releases: the latest Android release may have no Windows asset.
        for (var page = 1; page <= 5; page++)
        {
            using var request = await RequestAsync($"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}", false);
            using var response = await http.SendAsync(request, timeout.Token);
            EnsureSuccess(response);
            var batch = await response.Content.ReadFromJsonAsync<List<WindowsRelease>>(timeout.Token) ?? [];
            releases.AddRange(batch);
            if (batch.Count < 100) break;
        }
        return WindowsRelease.Latest(releases, AppInfo.Current.VersionString, Architecture);
    }
    public static async Task<string> DownloadAsync(WindowsRelease release, IProgress<double> progress, CancellationToken ct)
    {
        var asset = release.Package(Architecture) ?? throw new InvalidDataException("Pacote Windows inválido.");
        var folder = Path.Combine(FileSystem.CacheDirectory, "windows-updates");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid() + ".zip");
        try
        {
            using var request = await RequestAsync(asset.Url + "?cache=" + Guid.NewGuid().ToString("N"), true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromHours(2));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            EnsureSuccess(response);
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using (var output = File.Create(path))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    received += count;
                    if (received > asset.Size) throw new InvalidDataException("O pacote excede o tamanho publicado.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    hash.AppendData(buffer, 0, count);
                    progress.Report((double)received / asset.Size);
                }
                if (received != asset.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(asset.Digest[7..])))
                    throw new InvalidDataException("A validação de tamanho/SHA-256 falhou. O pacote não será instalado.");
            }
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public static async Task InstallAsync(string archive)
    {
        var target = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (!File.Exists(Path.Combine(target, ".psiptv-portable")))
            throw new InvalidOperationException("Para ativar as atualizações, extraia primeiro o ZIP Windows do GitHub para uma pasta sua e execute PSiptv.exe nessa pasta.");
        var stage = target + ".update-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            ZipFile.ExtractToDirectory(archive, stage);
            if (!File.Exists(Path.Combine(stage, "PSiptv.exe")) || !File.Exists(Path.Combine(stage, ".psiptv-portable")))
                throw new InvalidDataException("O pacote não contém uma distribuição Windows válida.");
            var script = Path.Combine(FileSystem.CacheDirectory, "apply-windows-update.ps1");
            await using (var input = await FileSystem.OpenAppPackageFileAsync("apply-windows-update.ps1"))
            await using (var output = File.Create(script)) await input.CopyToAsync(output);
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = FileSystem.CacheDirectory };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                         "-Target", target, "-Stage", stage, "-ParentProcess", Environment.ProcessId.ToString(CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar a atualização.");
        }
        catch { Directory.Delete(stage, true); throw; }
        Application.Current!.Quit();
    }
    public static async Task CheckOnStartupAsync(Page owner)
    {
        if (Interlocked.Exchange(ref started, 1) != 0 || !AutomaticChecksEnabled) return;
        var last = Preferences.Default.Get("windows-updates.last", 0L);
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - last < 86400) return;
        try
        {
            var release = await CheckLatestAsync(CancellationToken.None);
            Preferences.Default.Set("windows-updates.last", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (release is not null && owner.Window is not null && await owner.DisplayAlertAsync("Atualização disponível",
                $"Está disponível a versão {release.Tag} para Windows.", "Ver atualização", "Mais tarde"))
                await owner.Navigation.PushAsync(new WindowsAppUpdatePage(release));
        }
        catch { /* Manual checks display connection and authentication errors. */ }
    }
}
