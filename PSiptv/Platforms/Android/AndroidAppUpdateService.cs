using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

public sealed record AndroidAppRelease(
    string Tag,
    string Title,
    string Notes,
    DateTimeOffset? PublishedAt,
    string AssetName,
    long AssetSize,
    string AssetApiUrl,
    string? Digest);

public readonly record struct AppUpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
}

public enum InstallRequestResult { InstallerOpened, PermissionRequired }

public static class AndroidAppUpdateService
{
    private const string LastAutomaticCheckKey = "github-releases.last-check";
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromDays(1);
    private static readonly SemaphoreSlim checkGate = new(1, 1);
    private static readonly HttpClient http = CreateHttpClient();
    private static string? pendingInstallPath;
    private static int startupCheckStarted;

    public static string Repository => $"{Metadata("GitHubReleaseOwner")}/{Metadata("GitHubReleaseRepository")}";
    public static bool AutomaticChecksEnabled
    {
        get => Preferences.Default.Get("github-releases.auto-check", true);
        set => Preferences.Default.Set("github-releases.auto-check", value);
    }

    public static async Task<bool> HasTokenAsync() =>
        !string.IsNullOrWhiteSpace(await GitHubUpdateTokenStore.ReadAsync());

    public static async Task SaveTokenAsync(string token)
    {
        await GitHubUpdateTokenStore.SaveAsync(token);
        Preferences.Default.Remove(LastAutomaticCheckKey);
    }

    public static void RemoveToken()
    {
        GitHubUpdateTokenStore.Remove();
        Preferences.Default.Remove(LastAutomaticCheckKey);
    }

    public static async Task<AndroidAppRelease?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var token = await ReadTokenAsync();
        await checkGate.WaitAsync(cancellationToken);
        try
        {
            var owner = Uri.EscapeDataString(Metadata("GitHubReleaseOwner"));
            var repository = Uri.EscapeDataString(Metadata("GitHubReleaseRepository"));
            using var request = ApiRequest(HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{repository}/releases/latest", token);
            using var response = await SendAsync(request, cancellationToken);
            await EnsureGitHubSuccessAsync(response, cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("O GitHub devolveu uma Release inválida.");

            if (!ReleaseVersion.TryParse(release.TagName, out var available))
                throw new InvalidOperationException($"A tag «{release.TagName}» não é uma versão válida (ex.: v1.3.0).");
            if (!ReleaseVersion.TryParse(AppInfo.Current.VersionString, out var installed))
                throw new InvalidOperationException("A versão instalada não pode ser comparada com a Release.");
            if (available.CompareTo(installed) <= 0) return null;

            var releaseVersion = release.TagName.Trim().TrimStart('v', 'V');
            var expectedName = Metadata("GitHubReleaseAssetName")
                .Replace("{version}", releaseVersion, StringComparison.OrdinalIgnoreCase);
            var uploadedApks = release.Assets.Where(candidate =>
                candidate.State.Equals("uploaded", StringComparison.OrdinalIgnoreCase) &&
                candidate.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)).ToArray();
            var asset = uploadedApks.FirstOrDefault(candidate =>
                candidate.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
            // A Release with exactly one uploaded APK is unambiguous and also
            // supports older or manually named assets.
            asset ??= uploadedApks.Length == 1 ? uploadedApks[0] : null;
            if (asset is null)
                throw new InvalidOperationException($"A Release {release.TagName} não contém o APK esperado «{expectedName}».");
            if (asset.Size <= 0 || string.IsNullOrWhiteSpace(asset.Url))
                throw new InvalidOperationException("O APK publicado na Release está incompleto.");

            return new AndroidAppRelease(release.TagName, string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                release.Body ?? "", release.PublishedAt, asset.Name, asset.Size, asset.Url, asset.Digest);
        }
        finally { checkGate.Release(); }
    }

    public static async Task<string> DownloadAsync(AndroidAppRelease release,
        IProgress<AppUpdateDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var token = await ReadTokenAsync();
        var updateFolder = Path.Combine(FileSystem.CacheDirectory, "updates");
        Directory.CreateDirectory(updateFolder);
        var destination = Path.Combine(updateFolder, Path.GetFileName(release.AssetName));
        // Keep the temporary file's .apk suffix because Android package parsers
        // on some OEM builds reject archives with an unknown final extension.
        var temporary = Path.Combine(updateFolder,
            Path.GetFileNameWithoutExtension(release.AssetName) + ".download.apk");
        TryDelete(temporary);

        try
        {
            using var request = ApiRequest(HttpMethod.Get, release.AssetApiUrl, token, "application/octet-stream");
            using var response = await SendAsync(request, cancellationToken);
            await EnsureGitHubSuccessAsync(response, cancellationToken);
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long received = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                hash.AppendData(buffer, 0, count);
                received += count;
                progress?.Report(new(received, release.AssetSize));
            }
            await output.FlushAsync(cancellationToken);
            if (received != release.AssetSize)
                throw new InvalidDataException($"O APK recebido tem {received} bytes; eram esperados {release.AssetSize}.");

            var actualDigest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (release.Digest is { Length: > 0 } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualDigest), Convert.FromHexString(digest[7..])))
                throw new InvalidDataException("A verificação SHA-256 do APK falhou.");

            ValidateApk(temporary);
            File.Move(temporary, destination, true);
            return destination;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static async Task<InstallRequestResult> RequestInstallAsync(string apkPath)
    {
        return await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var activity = Platform.CurrentActivity as MainActivity
                ?? throw new InvalidOperationException("Não foi possível abrir o instalador Android.");
            if (!activity.PackageManager!.CanRequestPackageInstalls())
            {
                pendingInstallPath = apkPath;
                using var settings = new Intent(Settings.ActionManageUnknownAppSources,
                    Android.Net.Uri.Parse("package:" + activity.PackageName));
                activity.StartActivity(settings);
                return InstallRequestResult.PermissionRequired;
            }
            OpenInstaller(activity, apkPath);
            return InstallRequestResult.InstallerOpened;
        });
    }

    internal static void TryResumePendingInstall(MainActivity activity)
    {
        var path = pendingInstallPath;
        if (path is null || !File.Exists(path) || activity.PackageManager?.CanRequestPackageInstalls() != true) return;
        pendingInstallPath = null;
        OpenInstaller(activity, path);
    }

    public static async Task CheckOnStartupAsync(Page owner)
    {
        if (Interlocked.Exchange(ref startupCheckStarted, 1) != 0 || !AutomaticChecksEnabled || !await HasTokenAsync()) return;
        if (DateTimeOffset.TryParse(Preferences.Default.Get(LastAutomaticCheckKey, ""), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var last) && DateTimeOffset.UtcNow - last < AutomaticCheckInterval) return;
        Preferences.Default.Set(LastAutomaticCheckKey, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            var release = await CheckLatestAsync();
            if (release is null || owner.Window is null) return;
            var install = await owner.DisplayAlertAsync("Atualização disponível",
                $"Está disponível a versão {release.Tag}. Tem instalada a versão {AppInfo.Current.VersionString}.",
                "Ver atualização", "Mais tarde");
            if (install && owner.Window is not null)
                await owner.Navigation.PushAsync(new AndroidAppUpdatePage(release));
        }
        catch
        {
            // Automatic checks stay silent; manual checks expose actionable errors.
        }
    }

    private static void ValidateApk(string path)
    {
        var context = Android.App.Application.Context;
        var packageManager = context.PackageManager
            ?? throw new InvalidOperationException("Não foi possível validar o APK.");
        var archive = packageManager.GetPackageArchiveInfo(path, PackageInfoFlags.Activities)
            ?? throw new InvalidDataException("O ficheiro descarregado não é um APK Android válido.");
        if (!string.Equals(archive.PackageName, context.PackageName, StringComparison.Ordinal))
            throw new InvalidDataException("O APK pertence a outra aplicação e não será instalado.");
        var installed = packageManager.GetPackageInfo(context.PackageName!, PackageInfoFlags.Activities)
            ?? throw new InvalidOperationException("Não foi possível ler a versão Android instalada.");
        var archiveCode = OperatingSystem.IsAndroidVersionAtLeast(28) ? archive.LongVersionCode : archive.VersionCode;
        var installedCode = OperatingSystem.IsAndroidVersionAtLeast(28) ? installed.LongVersionCode : installed.VersionCode;
        if (archiveCode <= installedCode)
            throw new InvalidDataException("O versionCode do APK não é superior ao da aplicação instalada.");
    }

    private static void OpenInstaller(MainActivity activity, string apkPath)
    {
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            activity, activity.PackageName + ".updates", new Java.IO.File(apkPath));
        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        activity.StartActivity(intent);
    }

    private static HttpRequestMessage ApiRequest(HttpMethod method, string url, string token,
        string accept = "application/vnd.github+json")
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }

    private static async Task EnsureGitHubSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "O token GitHub é inválido ou expirou.",
            HttpStatusCode.Forbidden => "O token GitHub não tem permissão para ler as Releases.",
            HttpStatusCode.NotFound => "O repositório privado ou a Release não está acessível com este token.",
            _ => $"O GitHub devolveu o erro HTTP {(int)response.StatusCode}."
        };
        _ = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(message);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "O GitHub demorou demasiado tempo a responder. Verifique a ligação à Internet e tente novamente.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Não foi possível contactar api.github.com. Verifique a ligação à Internet, DNS, VPN ou certificado TLS do dispositivo.", ex);
        }
    }

    private static async Task<string> ReadTokenAsync() =>
        (await GitHubUpdateTokenStore.ReadAsync()) is { Length: > 0 } token
            ? token : throw new InvalidOperationException("Configure primeiro um token GitHub para o repositório privado.");

    private static string Metadata(string name) => typeof(AndroidAppUpdateService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == name)?.Value is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"A configuração Android «{name}» não foi definida.");

    private static HttpClient CreateHttpClient()
    {
        // Large APKs on slow connections may take longer than 20 minutes.
        // The foreground service owns the transfer until it completes.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
            { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PSiptv-Android-Updater/1.0");
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; init; } = "";
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; init; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("state")] public string State { get; init; } = "";
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("url")] public string Url { get; init; } = "";
        [JsonPropertyName("digest")] public string? Digest { get; init; }
    }
}
