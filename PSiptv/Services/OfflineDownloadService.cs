using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class OfflineDownloadService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly SemaphoreSlim slots = new(2, 2);
    private static readonly Dictionary<string, CancellationTokenSource> active = [];
    private static readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private const string DownloadsDirectoryKey = "offline.downloadsDirectory";
    private const string DownloadsTreeUriKey = "offline.downloadsTreeUri";
    public static event Action? Changed;
    public static int ActiveCount { get { lock (active) return active.Count; } }
    public static string DefaultDownloadsDirectory => Path.Combine(FileSystem.AppDataDirectory, "offline-vod");
    public static string DownloadsDirectory
    {
        get
        {
            var selected = Preferences.Default.Get(DownloadsDirectoryKey, "");
            return Path.IsPathFullyQualified(selected) ? selected : DefaultDownloadsDirectory;
        }
    }
    public static string DownloadsFolderLocation
    {
        get
        {
#if ANDROID
            var uri = Preferences.Default.Get(DownloadsTreeUriKey, "");
            if (uri.Length > 0) return uri;
#endif
            return DownloadsDirectory;
        }
    }

    public static string DownloadLocation(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
#if ANDROID
        if (GetDownloadsTree() is { } tree && tree.FindFile(safeName) is { } document)
            return document.Uri?.ToString() ?? "";
#endif
        var path = Path.Combine(DownloadsDirectory, safeName);
        return File.Exists(path) ? path : "";
    }

    public static (long Bytes, int Files) MeasureDownloadStorage()
    {
#if ANDROID
        if (GetDownloadsTree() is { } tree)
        {
            var files = (tree.ListFiles() ?? []).Where(file => file.IsFile).ToArray();
            return (files.Sum(file => Math.Max(0, file.Length())), files.Length);
        }
#endif
        long bytes = 0;
        var count = 0;
        try
        {
            if (!Directory.Exists(DownloadsDirectory)) return (0, 0);
            foreach (var path in Directory.EnumerateFiles(DownloadsDirectory, "*", SearchOption.AllDirectories))
            { bytes += new FileInfo(path).Length; count++; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (bytes, count);
    }

    public static async Task ChangeDownloadsDirectoryAsync(string selectedDirectory)
    {
        if (string.IsNullOrWhiteSpace(selectedDirectory)) return;
        if (ActiveCount > 0)
            throw new InvalidOperationException("Pause os downloads em curso antes de alterar a pasta.");
        var current = Path.GetFullPath(DownloadsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var selected = Path.GetFullPath(selectedDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
#if ANDROID
        var selectedTreeUri = PSiptv.AndroidFolderGrant.LatestTreeUri ?? FindPersistedTreeUri(selected);
        PSiptv.AndroidFolderGrant.LatestTreeUri = null;
        if (string.IsNullOrWhiteSpace(selectedTreeUri))
            throw new UnauthorizedAccessException("Não foi possível conservar a autorização da pasta. Escolha novamente a pasta.");
        var selectedTree = GetTree(selectedTreeUri);
        if (selectedTree is null || !selectedTree.CanWrite())
            throw new UnauthorizedAccessException("A pasta escolhida não permite escrita.");
        var probe = selectedTree.CreateFile("application/octet-stream", $".psiptv-write-test-{Guid.NewGuid():N}")
            ?? throw new UnauthorizedAccessException("Não foi possível criar ficheiros na pasta escolhida.");
        if (!probe.Delete()) throw new IOException("Não foi possível validar a escrita na pasta escolhida.");
        if (string.Equals(current, selected, comparison))
        {
            Preferences.Default.Set(DownloadsTreeUriKey, selectedTreeUri);
            Preferences.Default.Set(DownloadsDirectoryKey, selected);
            Changed?.Invoke();
            return;
        }
        var oldTree = GetDownloadsTree();
#else
        if (string.Equals(current, selected, comparison)) return;
        Directory.CreateDirectory(selected);
#endif
        var managedNames = await ManagedFileNamesAsync();
        var copied = new List<string>();
        await gate.WaitAsync();
        try
        {
#if ANDROID
            foreach (var name in managedNames.Where(name => FileExistsAt(current, oldTree, name)))
            {
                if (selectedTree.FindFile(name) is not null)
                    throw new InvalidOperationException("A pasta escolhida já contém um ficheiro com o mesmo nome de um download.");
                await using var input = OpenReadStream(current, oldTree, name);
                var destination = selectedTree.CreateFile(MimeTypeFor(name), name)
                    ?? throw new IOException("Não foi possível criar um download na pasta escolhida.");
                await using var output = Android.App.Application.Context.ContentResolver!.OpenOutputStream(destination.Uri!, "w")
                    ?? throw new IOException("Não foi possível escrever na pasta escolhida.");
                copied.Add(name);
                await input.CopyToAsync(output);
            }
            Preferences.Default.Set(DownloadsTreeUriKey, selectedTreeUri);
#else
            if (Directory.Exists(current))
                foreach (var name in managedNames.Where(name => File.Exists(Path.Combine(current, name))))
                {
                    var source = Path.Combine(current, name);
                    var destination = Path.Combine(selected, name);
                    if (File.Exists(destination))
                        throw new InvalidOperationException("A pasta escolhida já contém um ficheiro com o mesmo nome de um download.");
                    copied.Add(name); File.Copy(source, destination);
                }
#endif
            Preferences.Default.Set(DownloadsDirectoryKey, selected);
        }
        catch
        {
#if ANDROID
            foreach (var name in copied) selectedTree.FindFile(name)?.Delete();
#else
            foreach (var name in copied) try { File.Delete(Path.Combine(selected, name)); } catch (IOException) { }
#endif
            throw;
        }
        finally { gate.Release(); }
#if ANDROID
        foreach (var name in copied) DeleteFileAt(current, oldTree, name);
#else
        foreach (var name in copied)
            try { File.Delete(Path.Combine(current, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
#endif
        Changed?.Invoke();
    }

    public static Task<IReadOnlyList<OfflineDownload>> LoadActiveProfileAsync() =>
        AppServices.ActiveAccount is { } account
            ? LoadAsync(account.Id, UserProfileService.Active.Id)
            : Task.FromResult<IReadOnlyList<OfflineDownload>>([]);

    public static async Task<IReadOnlyList<OfflineDownload>> LoadAsync(string accountId, string profileId)
    {
        await gate.WaitAsync();
        try
        {
            var items = await ReadAsync(accountId, profileId);
            var changed = false;
            for (var index = 0; index < items.Count; index++)
            {
                bool running;
                lock (active) running = active.ContainsKey(items[index].Id);
                if (items[index].State is OfflineDownloadState.Downloading or OfflineDownloadState.Queued && !running)
                {
                    items[index] = items[index] with { State = OfflineDownloadState.Paused, Error = "O download foi interrompido. Pode retomá-lo." };
                    changed = true;
                }
                if (items[index].IsComplete && !DownloadFileExists(items[index].FileName))
                {
                    items[index] = items[index] with { State = OfflineDownloadState.Failed, Error = "O ficheiro descarregado já não existe." };
                    changed = true;
                }
            }
            if (changed) await WriteAsync(accountId, profileId, items);
            return items.OrderByDescending(item => item.State == OfflineDownloadState.Downloading)
                .ThenByDescending(item => item.CreatedAt).ToArray();
        }
        finally { gate.Release(); }
    }

    public static async Task<OfflineDownload?> FindAsync(string accountId, string profileId, MediaItem item)
    {
        var id = OfflineDownloadPolicy.IdFor(accountId, profileId, item);
        return (await LoadAsync(accountId, profileId)).FirstOrDefault(download => download.Id == id);
    }

    public static async Task<OfflineDownload?> FindByIdAsync(string id)
    {
        await UserProfileService.LoadAsync();
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var profile in UserProfileService.Profiles)
            if ((await LoadAsync(account.Id, profile.Id)).FirstOrDefault(item => item.Id == id) is { } found)
                return found;
        return null;
    }

    public static MediaItem PlaybackItem(OfflineDownload download)
    {
        var path = DownloadLocation(download.FileName);
        if (!download.IsComplete || path.Length == 0)
            throw new InvalidOperationException("O ficheiro offline ainda não está disponível.");
        return download.Item with
        {
            Url = new Uri(path, UriKind.Absolute).AbsoluteUri, IsCatchup = true,
            SourceCommand = "", HttpReferer = "", HttpUserAgent = "", HttpCookie = ""
        };
    }

    public static async Task<OfflineDownload> QueueAsync(PlaylistAccount account, MediaItem item, string seriesName = "")
    {
#if ANDROID
        await PSiptv.AndroidNotificationPermission.RequestAsync();
#endif
        if (!OfflineDownloadPolicy.CanDownload(item))
            throw new InvalidOperationException("Este conteúdo não tem um endereço VOD descarregável.");
        var profileId = UserProfileService.Active.Id;
        var id = OfflineDownloadPolicy.IdFor(account.Id, profileId, item);
        OfflineDownload download;
        await gate.WaitAsync();
        try
        {
            var items = await ReadAsync(account.Id, profileId);
            var existing = items.FirstOrDefault(value => value.Id == id);
            if (existing?.IsComplete == true && DownloadFileExists(existing.FileName)) return existing;
            download = existing is null
                ? new(id, account.Id, profileId, item, seriesName, FileName: FileNameFor(item, id),
                    CreatedAt: DateTimeOffset.Now, UpdatedAt: DateTimeOffset.Now)
                : existing with { State = OfflineDownloadState.Queued, Error = "", UpdatedAt = DateTimeOffset.Now };
            items.RemoveAll(value => value.Id == id);
            items.Add(download);
            await WriteAsync(account.Id, profileId, items);
        }
        finally { gate.Release(); }
        Changed?.Invoke();
        Start(account, download);
        return download;
    }

    public static async Task ResumeAsync(OfflineDownload download)
    {
#if ANDROID
        await PSiptv.AndroidNotificationPermission.RequestAsync();
#endif
        var account = (await AppServices.Accounts.LoadAsync()).FirstOrDefault(item => item.Id == download.AccountId)
            ?? throw new InvalidOperationException("A lista associada a este download já não existe.");
        await SetStateAsync(download, OfflineDownloadState.Queued, "");
        Start(account, download);
    }

    public static async Task PauseAsync(OfflineDownload download)
    {
        CancellationTokenSource? cancellation;
        lock (active) active.TryGetValue(download.Id, out cancellation);
        cancellation?.Cancel();
        if (cancellation is null) await SetStateAsync(download, OfflineDownloadState.Paused, "");
    }

    public static async Task RemoveAsync(OfflineDownload download)
    {
        CancellationTokenSource? cancellation;
        lock (active) active.TryGetValue(download.Id, out cancellation);
        cancellation?.Cancel();
        await gate.WaitAsync();
        try
        {
            var items = await ReadAsync(download.AccountId, download.ProfileId);
            items.RemoveAll(item => item.Id == download.Id);
            await WriteAsync(download.AccountId, download.ProfileId, items);
            DeleteFile(download.FileName);
            DeleteFile(download.FileName + ".part");
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    public static async Task DeleteAccountAsync(string accountId)
    {
        foreach (var profile in UserProfileService.Profiles)
            await DeleteScopeAsync(accountId, profile.Id);
    }

    public static async Task DeleteAllAsync()
    {
        if (ActiveCount > 0)
            throw new InvalidOperationException("Pause os downloads em curso antes de eliminar todos os downloads.");
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var profile in UserProfileService.Profiles)
            await DeleteScopeAsync(account.Id, profile.Id);
        Changed?.Invoke();
    }

    public static Task DeleteProfileAsync(string accountId, string profileId) => DeleteScopeAsync(accountId, profileId);

    private static void Start(PlaylistAccount account, OfflineDownload download)
    {
#if ANDROID
        OfflineDownloadPlatform.Start(download.Id);
#else
        _ = StartAndWaitAsync(account, download);
#endif
    }

    public static async Task RunInBackgroundAsync(string downloadId)
    {
        await UserProfileService.LoadAsync();
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var profile in UserProfileService.Profiles)
        {
            var download = (await LoadAsync(account.Id, profile.Id)).FirstOrDefault(item => item.Id == downloadId);
            if (download is null || download.IsComplete) continue;
            await StartAndWaitAsync(account, download);
            return;
        }
    }

    private static Task StartAndWaitAsync(PlaylistAccount account, OfflineDownload download)
    {
        lock (active)
        {
            if (active.ContainsKey(download.Id)) return Task.CompletedTask;
            var cancellation = new CancellationTokenSource();
            active[download.Id] = cancellation;
            return RunAsync(account, download, cancellation);
        }
    }

    private static async Task RunAsync(PlaylistAccount account, OfflineDownload download, CancellationTokenSource cancellation)
    {
        var entered = false;
        try
        {
            await slots.WaitAsync(cancellation.Token);
            entered = true;
            await SetStateAsync(download, OfflineDownloadState.Downloading, "");
            var resolved = await AppServices.Client.ResolveStreamAsync(account, download.Item, cancellation.Token);
            var uri = WebAddress.Require(resolved.Url);
            if (uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Este VOD usa HLS segmentado e ainda não pode ser descarregado para reprodução offline.");

            PromoteLegacyPart(download.FileName);
            var existing = DownloadFileLength(download.FileName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", resolved.HttpUserAgent.Length > 0 ? resolved.HttpUserAgent : AppOptions.UserAgent);
            if (resolved.HttpReferer.Length > 0) request.Headers.TryAddWithoutValidation("Referer", resolved.HttpReferer);
            if (resolved.HttpCookie.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", resolved.HttpCookie);
            if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
                response.Content.Headers.ContentRange?.Length == existing)
            {
                await CompleteAsync(download, existing);
                return;
            }
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
                    ? "Este VOD usa HLS segmentado e ainda não pode ser descarregado para reprodução offline."
                    : "O endereço VOD devolveu uma página HTML em vez de vídeo.");
            var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!append) existing = 0;
            var total = response.Content.Headers.ContentRange?.Length ??
                        (response.Content.Headers.ContentLength is { } length ? length + existing : null);
            await UpdateProgressAsync(download, existing, total);
            await using var input = await response.Content.ReadAsStreamAsync(cancellation.Token);
            await using var output = OpenDownloadWriteStream(download.FileName, append);
            var buffer = new byte[128 * 1024];
            var downloaded = existing;
            var lastUpdate = Environment.TickCount64;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellation.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                downloaded += read;
                if (Environment.TickCount64 - lastUpdate >= 750)
                {
                    await UpdateProgressAsync(download, downloaded, total);
                    lastUpdate = Environment.TickCount64;
                }
            }
            await output.FlushAsync(cancellation.Token);
            await CompleteAsync(download, downloaded, total);
        }
        catch (OperationCanceledException)
        {
            await SetStateAsync(download, OfflineDownloadState.Paused, "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await SetStateAsync(download, OfflineDownloadState.Failed, SourceDiagnosticPolicy.SafeError(ex, account));
        }
        finally
        {
            if (entered) slots.Release();
            lock (active)
            {
                active.Remove(download.Id);
                cancellation.Dispose();
            }
            Changed?.Invoke();
        }
    }

    private static async Task CompleteAsync(OfflineDownload download, long bytes, long? total = null)
    {
        await UpdateAsync(download.AccountId, download.ProfileId, download.Id, item => item with
        {
            State = OfflineDownloadState.Completed, BytesDownloaded = bytes,
            TotalBytes = total ?? bytes, Error = "", UpdatedAt = DateTimeOffset.Now
        });
    }

    private static Task UpdateProgressAsync(OfflineDownload download, long bytes, long? total) =>
        UpdateAsync(download.AccountId, download.ProfileId, download.Id, item => item with
        { BytesDownloaded = bytes, TotalBytes = total, UpdatedAt = DateTimeOffset.Now });

    private static Task SetStateAsync(OfflineDownload download, OfflineDownloadState state, string error) =>
        UpdateAsync(download.AccountId, download.ProfileId, download.Id, item => item with
        { State = state, Error = error, UpdatedAt = DateTimeOffset.Now });

    private static async Task UpdateAsync(string accountId, string profileId, string id,
        Func<OfflineDownload, OfflineDownload> update)
    {
        await gate.WaitAsync();
        try
        {
            var items = await ReadAsync(accountId, profileId);
            var index = items.FindIndex(item => item.Id == id);
            if (index < 0) return;
            items[index] = update(items[index]);
            await WriteAsync(accountId, profileId, items);
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    private static async Task DeleteScopeAsync(string accountId, string profileId)
    {
        var items = await LoadAsync(accountId, profileId);
        foreach (var item in items) await RemoveAsync(item);
        SecureStorage.Default.Remove(StorageKey(accountId, profileId));
    }

    private static async Task<string[]> ManagedFileNamesAsync()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var profile in UserProfileService.Profiles)
        foreach (var download in await ReadAsync(account.Id, profile.Id))
        {
            if (download.FileName.Length == 0) continue;
            names.Add(Path.GetFileName(download.FileName));
            names.Add(Path.GetFileName(download.FileName) + ".part");
        }
        return names.ToArray();
    }

    private static string FileNameFor(MediaItem item, string id) =>
        $"{OfflineDownloadPolicy.SafeFileStem(item.Name)}-{id[..8]}{OfflineDownloadPolicy.ExtensionFor(item)}";

    private static string PathFor(string fileName) => Path.Combine(DownloadsDirectory, Path.GetFileName(fileName));

    private static bool DownloadFileExists(string fileName)
    {
#if ANDROID
        if (GetDownloadsTree() is { } tree) return tree.FindFile(Path.GetFileName(fileName)) is not null;
#endif
        return File.Exists(PathFor(fileName));
    }

    private static long DownloadFileLength(string fileName)
    {
#if ANDROID
        if (GetDownloadsTree() is { } tree) return tree.FindFile(Path.GetFileName(fileName))?.Length() ?? 0;
#endif
        var path = PathFor(fileName);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static Stream OpenDownloadWriteStream(string fileName, bool append)
    {
#if ANDROID
        if (GetDownloadsTree() is { } tree)
        {
            var document = tree.FindFile(Path.GetFileName(fileName));
            if (!append)
            {
                document?.Delete();
                document = null;
            }
            document ??= tree.CreateFile(MimeTypeFor(fileName), Path.GetFileName(fileName));
            if (document is null)
                throw new UnauthorizedAccessException("Não foi possível criar o ficheiro na pasta de downloads.");
            return Android.App.Application.Context.ContentResolver!
                .OpenOutputStream(document.Uri!, append ? "wa" : "w")
                ?? throw new UnauthorizedAccessException("Não foi possível escrever na pasta de downloads.");
        }
        if (Path.IsPathFullyQualified(Preferences.Default.Get(DownloadsDirectoryKey, "")))
            throw new UnauthorizedAccessException("A autorização da pasta de downloads expirou. Escolha novamente a pasta.");
#endif
        Directory.CreateDirectory(DownloadsDirectory);
        return new FileStream(PathFor(fileName), append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.Read, 128 * 1024, true);
    }

    private static void PromoteLegacyPart(string fileName)
    {
        var partName = Path.GetFileName(fileName) + ".part";
        if (DownloadFileExists(fileName) || !DownloadFileExists(partName)) return;
#if ANDROID
        if (GetDownloadsTree() is { } tree)
        {
            tree.FindFile(partName)?.RenameTo(Path.GetFileName(fileName));
            return;
        }
#endif
        File.Move(PathFor(partName), PathFor(fileName), true);
    }

    private static void DeleteFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;
        try
        {
#if ANDROID
            if (GetDownloadsTree() is { } tree)
            {
                tree.FindFile(Path.GetFileName(fileName))?.Delete();
                return;
            }
#endif
            var root = Path.GetFullPath(DownloadsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(PathFor(fileName));
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) File.Delete(target);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

#if ANDROID
    private static AndroidX.DocumentFile.Provider.DocumentFile? GetDownloadsTree() =>
        GetTree(Preferences.Default.Get(DownloadsTreeUriKey, ""));

    private static AndroidX.DocumentFile.Provider.DocumentFile? GetTree(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText)) return null;
        var uri = Android.Net.Uri.Parse(uriText);
        return uri is null ? null : AndroidX.DocumentFile.Provider.DocumentFile.FromTreeUri(
            Android.App.Application.Context, uri);
    }

    private static string? FindPersistedTreeUri(string selectedPath)
    {
        var normalized = selectedPath.Replace('\\', '/').TrimEnd('/');
        foreach (var permission in Android.App.Application.Context.ContentResolver?.PersistedUriPermissions ?? [])
        {
            var uri = permission.Uri;
            if (uri is null || !permission.IsWritePermission) continue;
            var documentId = Android.Provider.DocumentsContract.GetTreeDocumentId(uri) ?? "";
            var separator = documentId.IndexOf(':');
            if (separator < 0) continue;
            var volume = documentId[..separator];
            var relative = documentId[(separator + 1)..].Replace('\\', '/').Trim('/');
            var root = volume.Equals("primary", StringComparison.OrdinalIgnoreCase)
                ? "/storage/emulated/0" : "/storage/" + volume;
            if (string.Equals(root + (relative.Length == 0 ? "" : "/" + relative), normalized,
                    StringComparison.Ordinal)) return uri.ToString();
        }
        return null;
    }

    private static bool FileExistsAt(string directory, AndroidX.DocumentFile.Provider.DocumentFile? tree, string fileName) =>
        tree?.FindFile(fileName) is not null || tree is null && File.Exists(Path.Combine(directory, fileName));

    private static Stream OpenReadStream(string directory,
        AndroidX.DocumentFile.Provider.DocumentFile? tree, string fileName)
    {
        if (tree?.FindFile(fileName) is { } document)
            return Android.App.Application.Context.ContentResolver!.OpenInputStream(document.Uri!)
                ?? throw new IOException("Não foi possível ler um download existente.");
        return File.OpenRead(Path.Combine(directory, fileName));
    }

    private static void DeleteFileAt(string directory, AndroidX.DocumentFile.Provider.DocumentFile? tree, string fileName)
    {
        if (tree is not null)
        {
            tree.FindFile(fileName)?.Delete();
            return;
        }
        try { File.Delete(Path.Combine(directory, fileName)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string MimeTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".mkv" => "video/x-matroska", ".webm" => "video/webm", ".ts" => "video/mp2t",
        ".avi" => "video/x-msvideo", ".mov" => "video/quicktime", ".m4v" => "video/x-m4v",
        _ => "video/mp4"
    };
#endif

    private static async Task<List<OfflineDownload>> ReadAsync(string accountId, string profileId)
    {
        var json = await SecureStorage.Default.GetAsync(StorageKey(accountId, profileId));
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<OfflineDownload>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Task WriteAsync(string accountId, string profileId, IEnumerable<OfflineDownload> downloads) =>
        SecureStorage.Default.SetAsync(StorageKey(accountId, profileId), JsonSerializer.Serialize(downloads));

    private static string StorageKey(string accountId, string profileId) =>
        $"offline-downloads.{UserProfileService.Scope(accountId, profileId)}";
}

public static partial class OfflineDownloadPlatform
{
    public static void Start(string downloadId) => StartCore(downloadId);
    static partial void StartCore(string downloadId);
}
