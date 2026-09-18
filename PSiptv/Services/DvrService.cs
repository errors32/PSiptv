using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class DvrService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly SemaphoreSlim recurringGate = new(1, 1);
    private static readonly Dictionary<string, CancellationTokenSource> active = [];
    private static readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static Timer? timer;
    private static DateTimeOffset lastRecurringRefresh = DateTimeOffset.MinValue;
    private const string RecordingsDirectoryKey = "dvr.recordingsDirectory";
    private const string RecordingsTreeUriKey = "dvr.recordingsTreeUri";
    public static event Action? Changed;

    public static int ActiveCount { get { lock (active) return active.Count; } }
    public static string DefaultRecordingsDirectory => Path.Combine(FileSystem.AppDataDirectory, "recordings");
    public static string RecordingsDirectory
    {
        get
        {
            var selected = Preferences.Default.Get(RecordingsDirectoryKey, "");
            return Path.IsPathFullyQualified(selected) ? selected : DefaultRecordingsDirectory;
        }
    }

    public static bool UsesCustomRecordingsDirectory =>
        Path.IsPathFullyQualified(Preferences.Default.Get(RecordingsDirectoryKey, ""));

    public static string RecordingLocation(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
#if ANDROID
        if (GetRecordingsTree() is { } tree && tree.FindFile(safeName) is { } document)
            return document.Uri?.ToString() ?? "";
#endif
        return Path.Combine(RecordingsDirectory, safeName);
    }

    public static string RecordingsFolderLocation
    {
        get
        {
#if ANDROID
            var uri = Preferences.Default.Get(RecordingsTreeUriKey, "");
            if (uri.Length > 0) return uri;
#endif
            return RecordingsDirectory;
        }
    }

    public static (long Bytes, int Files) MeasureRecordingStorage()
    {
#if ANDROID
        if (GetRecordingsTree() is { } tree)
        {
            var files = (tree.ListFiles() ?? []).Where(file => file.IsFile).ToArray();
            return (files.Sum(file => Math.Max(0, file.Length())), files.Length);
        }
#endif
        long bytes = 0;
        var count = 0;
        try
        {
            if (!Directory.Exists(RecordingsDirectory)) return (0, 0);
            foreach (var path in Directory.EnumerateFiles(RecordingsDirectory, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(path).Length; count++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (bytes, count);
    }

    public static Task<bool> EnsureRecordingsDirectoryAccessAsync(bool force = false)
    {
#if ANDROID
        if (!UsesCustomRecordingsDirectory) return Task.FromResult(true);
        var savedUri = Preferences.Default.Get(RecordingsTreeUriKey, "");
        if (savedUri.Length == 0) return Task.FromResult(false);
        var hasPersistedWriteGrant = (Android.App.Application.Context.ContentResolver?.PersistedUriPermissions ?? [])
            .Any(permission => permission.IsWritePermission && permission.Uri?.ToString() == savedUri);
        return Task.FromResult(hasPersistedWriteGrant && GetRecordingsTree()?.CanWrite() == true);
#else
        return Task.FromResult(true);
#endif
    }

    public static async Task ChangeRecordingsDirectoryAsync(string selectedDirectory)
    {
        if (string.IsNullOrWhiteSpace(selectedDirectory)) return;
        if (ActiveCount > 0)
            throw new InvalidOperationException("Pare as gravações em curso antes de alterar a pasta.");

        var current = Path.GetFullPath(RecordingsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var selected = Path.GetFullPath(selectedDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
#if !ANDROID
        if (string.Equals(current, selected, pathComparison)) return;
#endif

#if ANDROID
        var selectedTreeUri = PSiptv.AndroidFolderGrant.LatestTreeUri ?? FindPersistedTreeUri(selected);
        PSiptv.AndroidFolderGrant.LatestTreeUri = null;
        if (string.IsNullOrWhiteSpace(selectedTreeUri))
            throw new UnauthorizedAccessException("Não foi possível conservar a autorização da pasta. Escolha novamente a pasta no seletor do Android.");
        var selectedTree = GetTree(selectedTreeUri);
        if (selectedTree is null || !selectedTree.CanWrite())
            throw new UnauthorizedAccessException("A pasta escolhida não permite escrita.");
        var probeName = $".psiptv-write-test-{Guid.NewGuid():N}";
        var probe = selectedTree.CreateFile("application/octet-stream", probeName)
            ?? throw new UnauthorizedAccessException("Não foi possível criar ficheiros na pasta escolhida.");
        if (!probe.Delete()) throw new IOException("Não foi possível validar a escrita na pasta escolhida.");
        if (string.Equals(current, selected, pathComparison))
        {
            Preferences.Default.Set(RecordingsTreeUriKey, selectedTreeUri);
            Preferences.Default.Set(RecordingsDirectoryKey, selected);
            Changed?.Invoke();
            return;
        }
#else
        Directory.CreateDirectory(selected);
#endif
        var copied = new List<string>();
#if ANDROID
        var oldTree = GetRecordingsTree();
#endif
        await gate.WaitAsync();
        try
        {
#if ANDROID
            foreach (var fileName in EnumerateRecordingFileNames(current, oldTree))
            {
                if (selectedTree.FindFile(fileName) is not null)
                    throw new InvalidOperationException("A pasta escolhida já contém um ficheiro com o mesmo nome de uma gravação.");
                await using var input = OpenRecordingReadStream(current, oldTree, fileName);
                var destination = selectedTree.CreateFile(MimeTypeFor(fileName), fileName)
                    ?? throw new IOException("Não foi possível criar uma gravação na pasta escolhida.");
                await using var output = Android.App.Application.Context.ContentResolver!
                    .OpenOutputStream(destination.Uri!, "w")
                    ?? throw new IOException("Não foi possível escrever na pasta escolhida.");
                await input.CopyToAsync(output);
                copied.Add(fileName);
            }
            Preferences.Default.Set(RecordingsTreeUriKey, selectedTreeUri);
#else
            if (Directory.Exists(current))
            {
                foreach (var source in Directory.EnumerateFiles(current))
                {
                    var destination = Path.Combine(selected, Path.GetFileName(source));
                    if (File.Exists(destination))
                        throw new InvalidOperationException("A pasta escolhida já contém um ficheiro com o mesmo nome de uma gravação.");
                    File.Copy(source, destination);
                    copied.Add(destination);
                }
            }
#endif

            Preferences.Default.Set(RecordingsDirectoryKey, selected);
        }
        catch
        {
#if ANDROID
            foreach (var name in copied) selectedTree.FindFile(name)?.Delete();
#else
            foreach (var path in copied)
                try { File.Delete(path); } catch (IOException) { }
#endif
            throw;
        }
        finally { gate.Release(); }

        // The new copies are already the active recordings at this point. Failure to
        // remove an old copy must never roll back or delete the successfully moved data.
#if ANDROID
        DeleteAllFiles(current, oldTree);
#else
        foreach (var source in Directory.Exists(current) ? Directory.EnumerateFiles(current).ToArray() : [])
            try { File.Delete(source); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
#endif
        Changed?.Invoke();
    }

    public static async Task<IReadOnlyList<DvrRecording>> LoadAsync(string accountId, string? profileId = null)
    {
        var selectedProfile = profileId ?? UserProfileService.Active.Id;
        var items = await ReadAsync(accountId, selectedProfile);
        var changed = false;
        for (var i = 0; i < items.Count; i++)
        {
            bool isActive;
            lock (active) isActive = active.ContainsKey(items[i].Id);
            if (items[i].State == DvrRecordingState.Recording && !isActive)
            {
                items[i] = items[i] with
                {
                    State = items[i].End > DateTimeOffset.Now ? DvrRecordingState.Scheduled : DvrRecordingState.Failed,
                    Error = items[i].End > DateTimeOffset.Now ? "" : "A aplicação foi encerrada antes de concluir a gravação."
                };
                changed = true;
            }
            else if (DvrRecordingPolicy.IsMissed(items[i], DateTimeOffset.Now))
            {
                items[i] = items[i] with { State = DvrRecordingState.Failed, Error = "O horário da gravação já terminou." };
                changed = true;
            }
        }
        if (changed) await WriteAsync(accountId, selectedProfile, items);
        return items.Where(item => !item.Suppressed).OrderByDescending(item => item.IsRecording)
            .ThenBy(item => item.State == DvrRecordingState.Scheduled ? 0 : 1)
            .ThenBy(item => item.Start).ToArray();
    }

    public static async Task<DvrRecording?> FindByIdAsync(string id)
    {
        await UserProfileService.LoadAsync();
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var profile in UserProfileService.Profiles)
            if ((await LoadAsync(account.Id, profile.Id)).FirstOrDefault(item => item.Id == id) is { } found)
                return found;
        return null;
    }

    public static async Task<bool> ToggleProgrammeAsync(PlaylistAccount account, MediaItem channel, TvProgramme programme)
    {
        var profileId = UserProfileService.Active.Id;
        var id = DvrRecordingPolicy.IdFor(account.Id, profileId, channel.Id, programme.Title, programme.Start);
        var existing = (await LoadAsync(account.Id, profileId)).FirstOrDefault(item => item.Id == id);
        if (existing is { State: DvrRecordingState.Scheduled or DvrRecordingState.Recording })
        {
            await RemoveAsync(existing);
            return false;
        }
        if (existing is not null) await RemoveAsync(existing);
        if (!DvrRecordingPolicy.CanSchedule(programme.Start, programme.End, DateTimeOffset.Now))
            throw new InvalidOperationException("Este programa já terminou e não pode ser gravado.");
        var recording = new DvrRecording(id, account.Id, profileId, channel,
            programme.Title, programme.Start, programme.End);
        await AddAsync(recording);
        return true;
    }

    public static async Task<DvrRecording> StartManualAsync(PlaylistAccount account, MediaItem channel, TimeSpan duration)
    {
        if (channel.Kind != MediaKind.Channel || channel.Url.Length == 0)
            throw new InvalidOperationException("Só é possível gravar canais em direto.");
        var start = DateTimeOffset.Now;
        var end = start.Add(duration);
        var title = channel.Name;
        var recording = new DvrRecording(
            DvrRecordingPolicy.IdFor(account.Id, UserProfileService.Active.Id, channel.Id, title, start),
            account.Id, UserProfileService.Active.Id, channel, title, start, end);
        await AddAsync(recording);
        return recording;
    }

    public static async Task<IReadOnlyList<DvrSeriesRule>> LoadSeriesRulesAsync(string accountId, string? profileId = null) =>
        (await ReadRulesAsync(accountId, profileId ?? UserProfileService.Active.Id))
        .OrderBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public static async Task SaveSeriesRuleAsync(DvrSeriesRule rule)
    {
        if (DvrSeriesRulePolicy.Normalize(rule.TitlePattern).Length == 0)
            throw new InvalidOperationException("Indique o título a procurar no guia.");
#if ANDROID
        await PSiptv.AndroidNotificationPermission.RequestAsync();
#endif
        await gate.WaitAsync();
        try
        {
            var rules = await ReadRulesAsync(rule.AccountId, rule.ProfileId);
            rules.RemoveAll(item => item.Id == rule.Id);
            rules.Add(rule with { CreatedAt = rule.CreatedAt ?? DateTimeOffset.UtcNow });
            await WriteRulesAsync(rule.AccountId, rule.ProfileId, rules);
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    public static async Task DeleteSeriesRuleAsync(DvrSeriesRule rule)
    {
        await gate.WaitAsync();
        try
        {
            var rules = await ReadRulesAsync(rule.AccountId, rule.ProfileId);
            rules.RemoveAll(item => item.Id == rule.Id);
            await WriteRulesAsync(rule.AccountId, rule.ProfileId, rules);
            var recordings = await ReadAsync(rule.AccountId, rule.ProfileId);
            if (recordings.RemoveAll(item => item.SeriesRuleId == rule.Id && item.Suppressed) > 0)
                await WriteAsync(rule.AccountId, rule.ProfileId, recordings);
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    public static Task SetSeriesRuleEnabledAsync(DvrSeriesRule rule, bool enabled) =>
        SaveSeriesRuleAsync(rule with { Enabled = enabled });

    public static async Task<IReadOnlyList<DvrSeriesRule>> ExportSeriesRulesAsync(string accountId, string profileId) =>
        await ReadRulesAsync(accountId, profileId);

    public static async Task ImportSeriesRulesAsync(string accountId, string profileId,
        IReadOnlyList<DvrSeriesRule> imported)
    {
        await gate.WaitAsync();
        try
        {
            var rules = await ReadRulesAsync(accountId, profileId);
            var merged = rules.Concat(imported.Where(rule => rule.AccountId == accountId && rule.ProfileId == profileId))
                .GroupBy(rule => rule.Id).Select(group => group.Last()).ToArray();
            await WriteRulesAsync(accountId, profileId, merged);
        }
        finally { gate.Release(); }
    }

    public static async Task<int> RefreshRecurringAsync(PlaylistAccount account,
        IReadOnlyList<MediaItem> channels, string? profileId = null, CancellationToken token = default)
    {
        var rules = await ReadRulesAsync(account.Id, profileId ?? UserProfileService.Active.Id);
        return await ScanSeriesRulesAsync(account, channels, rules, token);
    }

    public static async Task RefreshAllRecurringAsync(CancellationToken token = default)
    {
        await UserProfileService.LoadAsync();
        foreach (var account in await AppServices.Accounts.LoadAsync())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var rules = new List<DvrSeriesRule>();
                foreach (var profile in UserProfileService.Profiles)
                    rules.AddRange(await ReadRulesAsync(account.Id, profile.Id));
                if (!rules.Any(rule => rule.Enabled)) continue;
                var catalogs = await CatalogCacheService.LoadAsync(account.Id);
                var channels = catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
                if (channels.Count == 0)
                    channels = await AppServices.Client.GetCatalogAsync(account, MediaKind.Channel, token);
                await ScanSeriesRulesAsync(account, channels, rules, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    private static async Task<int> ScanSeriesRulesAsync(PlaylistAccount account,
        IReadOnlyList<MediaItem> channels, IReadOnlyList<DvrSeriesRule> rules, CancellationToken token)
    {
        var enabled = rules.Where(rule => rule.Enabled).ToArray();
        if (enabled.Length == 0 || channels.Count == 0) return 0;
        await recurringGate.WaitAsync(token);
        try
        {
            var now = DateTimeOffset.Now;
            var guide = await AppServices.Client.GetFullGuideAsync(account, token);
            var programmes = guide.GroupBy(item => item.ChannelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var rule in enabled)
            foreach (var channel in channels.Where(item => item.Kind == MediaKind.Channel &&
                         (rule.ChannelId.Length == 0 || item.Id == rule.ChannelId)))
            {
                var epgId = channel.EpgId.Length > 0 ? channel.EpgId : channel.Id;
                if (!programmes.TryGetValue(epgId, out var schedule)) continue;
                foreach (var programme in schedule.Where(item =>
                             DvrSeriesRulePolicy.Matches(rule, channel, item, now, TimeSpan.FromDays(8))))
                    if (await ScheduleSeriesMatchAsync(rule, channel, programme)) added++;
            }
            if (added > 0) Changed?.Invoke();
            return added;
        }
        finally { recurringGate.Release(); }
    }

    private static async Task<bool> ScheduleSeriesMatchAsync(DvrSeriesRule rule,
        MediaItem channel, TvProgramme programme)
    {
        DvrRecording recording;
        await gate.WaitAsync();
        try
        {
            var items = await ReadAsync(rule.AccountId, rule.ProfileId);
            var id = DvrRecordingPolicy.IdFor(rule.AccountId, rule.ProfileId, channel.Id,
                programme.Title, programme.Start);
            var episodeKey = DvrSeriesRulePolicy.EpisodeKey(programme);
            if (items.Any(item => item.Id == id || item.SeriesRuleId == rule.Id &&
                    item.EpisodeKey == episodeKey && (item.Suppressed || item.State != DvrRecordingState.Failed))) return false;
            recording = new DvrRecording(id, rule.AccountId, rule.ProfileId, channel,
                programme.Title, programme.Start, programme.End,
                SeriesRuleId: rule.Id, EpisodeKey: episodeKey);
            items.Add(recording);
            await WriteAsync(rule.AccountId, rule.ProfileId, items);
        }
        finally { gate.Release(); }
        DvrPlatformScheduler.Schedule(recording, recording.Start > DateTimeOffset.Now
            ? recording.Start : DateTimeOffset.Now.AddSeconds(1));
        return true;
    }

    private static async Task AddAsync(DvrRecording recording)
    {
#if ANDROID
        await PSiptv.AndroidNotificationPermission.RequestAsync();
#endif
        await gate.WaitAsync();
        try
        {
            var items = await ReadAsync(recording.AccountId, recording.ProfileId);
            items.RemoveAll(item => item.Id == recording.Id);
            items.Add(recording);
            await WriteAsync(recording.AccountId, recording.ProfileId, items);
        }
        finally { gate.Release(); }
        DvrPlatformScheduler.Schedule(recording, recording.Start > DateTimeOffset.Now ? recording.Start : DateTimeOffset.Now.AddSeconds(1));
        Changed?.Invoke();
#if !ANDROID
        if (recording.Start <= DateTimeOffset.Now) _ = StartScheduledAsync(recording.Id);
#endif
    }

    public static async Task RemoveAsync(DvrRecording recording)
    {
        CancellationTokenSource? cancellation;
        lock (active) active.TryGetValue(recording.Id, out cancellation);
        cancellation?.Cancel();
        await gate.WaitAsync();
        try
        {
            var items = await ReadAsync(recording.AccountId, recording.ProfileId);
            var index = items.FindIndex(item => item.Id == recording.Id);
            if (index >= 0 && items[index].SeriesRuleId.Length > 0)
                items[index] = items[index] with
                {
                    State = DvrRecordingState.Failed, FileName = "", Bytes = 0,
                    Error = "", Suppressed = true
                };
            else items.RemoveAll(item => item.Id == recording.Id);
            await WriteAsync(recording.AccountId, recording.ProfileId, items);
        }
        finally { gate.Release(); }
        DvrPlatformScheduler.Cancel(recording.Id);
        DeleteFile(recording.FileName);
        Changed?.Invoke();
    }

    public static async Task StopAsync(DvrRecording recording)
    {
        CancellationTokenSource? cancellation;
        lock (active) active.TryGetValue(recording.Id, out cancellation);
        cancellation?.Cancel();
        await UpdateAsync(recording.Id, item => item with
        {
            State = DvrRecordingState.Failed,
            Error = "Gravação interrompida pelo utilizador."
        });
    }

    public static async Task DeleteAccountAsync(string accountId)
    {
        foreach (var profile in UserProfileService.Profiles)
            await DeleteProfileAsync(accountId, profile.Id);
    }

    public static async Task DeleteAllAsync()
    {
        if (ActiveCount > 0)
            throw new InvalidOperationException("Pare as gravações em curso antes de eliminar todas as gravações.");
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var profile in UserProfileService.Profiles)
            foreach (var item in await LoadAsync(account.Id, profile.Id))
                await RemoveAsync(item);
#if ANDROID
        DeleteAllFiles(RecordingsDirectory, GetRecordingsTree());
#else
        if (Directory.Exists(RecordingsDirectory))
            foreach (var path in Directory.EnumerateFiles(RecordingsDirectory))
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
#endif
        Changed?.Invoke();
    }

    public static async Task DeleteProfileAsync(string accountId, string profileId)
    {
        foreach (var item in await ReadAsync(accountId, profileId))
        {
            CancellationTokenSource? cancellation;
            lock (active) active.TryGetValue(item.Id, out cancellation);
            cancellation?.Cancel();
            DvrPlatformScheduler.Cancel(item.Id);
            DeleteFile(item.FileName);
        }
        SecureStorage.Default.Remove(StorageKey(accountId, profileId));
        SecureStorage.Default.Remove(RulesStorageKey(accountId, profileId));
    }

    public static async Task<IReadOnlyList<DvrRecording>> LoadActiveProfileAsync()
    {
        var result = new List<DvrRecording>();
        foreach (var account in await AppServices.Accounts.LoadAsync())
            result.AddRange(await LoadAsync(account.Id));
        return result.OrderByDescending(item => item.IsRecording)
            .ThenBy(item => item.IsScheduled ? 0 : 1).ThenBy(item => item.Start).ToArray();
    }

    public static async Task StartAsync()
    {
        await RescheduleAllAsync();
        timer ??= new Timer(_ => _ = RunDueAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    public static async Task RescheduleAllAsync()
    {
        await UserProfileService.LoadAsync();
        foreach (var profile in UserProfileService.Profiles)
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var recording in await LoadAsync(account.Id, profile.Id))
        {
            if (recording.IsScheduled)
                DvrPlatformScheduler.Schedule(recording,
                    recording.Start > DateTimeOffset.Now ? recording.Start : DateTimeOffset.Now.AddSeconds(1));
        }
        await RunDueAsync();
    }

    public static async Task RunDueAsync()
    {
        foreach (var profile in UserProfileService.Profiles)
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var recording in await LoadAsync(account.Id, profile.Id))
            if (DvrRecordingPolicy.IsDue(recording, DateTimeOffset.Now))
#if ANDROID
                DvrPlatformScheduler.Schedule(recording, DateTimeOffset.Now.AddSeconds(1));
#else
                _ = StartScheduledAsync(recording.Id);
#endif
        if (DateTimeOffset.UtcNow - lastRecurringRefresh >= TimeSpan.FromMinutes(30))
        {
            lastRecurringRefresh = DateTimeOffset.UtcNow;
            _ = RefreshAllRecurringSafelyAsync();
        }
    }

    private static async Task RefreshAllRecurringSafelyAsync()
    {
        try { await RefreshAllRecurringAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    public static async Task StartScheduledAsync(string recordingId)
    {
        await UserProfileService.LoadAsync();
        DvrRecording? recording = null;
        foreach (var account in await AppServices.Accounts.LoadAsync())
        foreach (var profile in UserProfileService.Profiles)
        {
            recording = (await LoadAsync(account.Id, profile.Id)).FirstOrDefault(item => item.Id == recordingId);
            if (recording is not null) goto Found;
        }
        Found:
        if (recording is null || !DvrRecordingPolicy.IsDue(recording, DateTimeOffset.Now)) return;

        var cancellation = new CancellationTokenSource();
        lock (active)
        {
            if (active.ContainsKey(recording.Id)) { cancellation.Dispose(); return; }
            active[recording.Id] = cancellation;
        }
        await UpdateAsync(recording.Id, item => item with { State = DvrRecordingState.Recording, Error = "" });
        var fileName = DvrRecordingPolicy.SafeFileStem(recording.ProgrammeTitle, recording.Start) + "_" + recording.Id[..8] + ".ts";
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            deadline.CancelAfter(DvrRecordingPolicy.Remaining(recording, DateTimeOffset.Now));
            var source = recording.Channel;
            var account = (await AppServices.Accounts.LoadAsync()).FirstOrDefault(item => item.Id == recording.AccountId);
            if (account is not null)
            {
                source = StreamPreferences.ApplyFormat(account, source, "ts");
                source = await AppServices.Client.ResolveStreamAsync(account, source, deadline.Token);
            }
            await using (var output = CreateRecordingFile(fileName))
                await RecordStreamAsync(new Uri(source.Url), output, recording.End, deadline.Token);
            var bytes = RecordingFileLength(fileName);
            if (bytes == 0) throw new InvalidOperationException("O fornecedor não enviou dados para a gravação.");
            await UpdateAsync(recording.Id, item => item with
            {
                State = DvrRecordingState.Completed, FileName = fileName, Bytes = bytes, Error = ""
            });
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested || DateTimeOffset.Now >= recording.End.AddSeconds(-2))
            {
                var bytes = RecordingFileLength(fileName);
                if (bytes > 0)
                {
                    await UpdateAsync(recording.Id, item => item with
                    {
                        State = DvrRecordingState.Completed, FileName = fileName,
                        Bytes = bytes, Error = ""
                    });
                }
            }
            else DeleteFile(fileName);
        }
        catch (Exception ex)
        {
            DeleteFile(fileName);
            await UpdateAsync(recording.Id, item => item with
            {
                State = DvrRecordingState.Failed,
                Error = ex is HttpRequestException ? "Não foi possível obter a transmissão do fornecedor." : ex.Message
            });
        }
        finally
        {
            lock (active) active.Remove(recording.Id);
            cancellation.Dispose();
            DvrPlatformScheduler.Cancel(recording.Id);
            Changed?.Invoke();
        }
    }

    private static async Task RecordStreamAsync(Uri source, Stream output, DateTimeOffset end, CancellationToken token)
    {
        if (source.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            await RecordHlsAsync(source, output, end, token);
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.UserAgent.ParseAdd(AppOptions.UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
        {
            await RecordHlsAsync(source, output, end, token);
            return;
        }
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await input.CopyToAsync(output, 128 * 1024, token);
    }

    private static async Task RecordHlsAsync(Uri source, Stream output, DateTimeOffset end, CancellationToken token)
    {
        var playlist = source;
        var downloaded = new HashSet<string>(StringComparer.Ordinal);
        while (DateTimeOffset.Now < end)
        {
            var text = await GetTextAsync(playlist, token);
            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
            if (lines.Any(line => line.StartsWith("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase) &&
                                  !line.Contains("METHOD=NONE", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Este canal usa HLS cifrado, que não pode ser gravado diretamente.");
            if (lines.Any(line => line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase)))
            {
                var variants = new List<(long Bandwidth, Uri Uri)>();
                for (var i = 0; i + 1 < lines.Length; i++)
                    if (lines[i].StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) && !lines[i + 1].StartsWith('#'))
                    {
                        var bandwidth = ParseBandwidth(lines[i]);
                        variants.Add((bandwidth, new Uri(playlist, lines[i + 1])));
                    }
                if (variants.Count == 0) throw new InvalidOperationException("A lista HLS não contém uma variante utilizável.");
                playlist = variants.OrderByDescending(item => item.Bandwidth).First().Uri;
                continue;
            }
            var map = lines.FirstOrDefault(line => line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase));
            if (map is not null)
            {
                var marker = "URI=\"";
                var start = map.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                var finish = start < 0 ? -1 : map.IndexOf('"', start + marker.Length);
                if (start >= 0 && finish > start)
                    await AppendSegmentAsync(new Uri(playlist, map[(start + marker.Length)..finish]), downloaded, output, token);
            }
            foreach (var line in lines.Where(line => !line.StartsWith('#')))
            {
                var segment = new Uri(playlist, line);
                await AppendSegmentAsync(segment, downloaded, output, token);
            }
            if (text.Contains("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase)) break;
            var target = lines.FirstOrDefault(line => line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.OrdinalIgnoreCase));
            var seconds = target is not null && int.TryParse(target[(target.IndexOf(':') + 1)..], out var value)
                ? Math.Clamp(value / 2, 1, 10) : 3;
            await Task.Delay(TimeSpan.FromSeconds(seconds), token);
        }
    }

    private static async Task AppendSegmentAsync(Uri segment, HashSet<string> downloaded,
        Stream output, CancellationToken token)
    {
        if (!downloaded.Add(segment.AbsoluteUri)) return;
        using var request = new HttpRequestMessage(HttpMethod.Get, segment);
        request.Headers.UserAgent.ParseAdd(AppOptions.UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await input.CopyToAsync(output, 128 * 1024, token);
        await output.FlushAsync(token);
    }

    private static async Task<string> GetTextAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(AppOptions.UserAgent);
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }

    private static long ParseBandwidth(string line)
    {
        const string marker = "BANDWIDTH=";
        var start = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return 0;
        start += marker.Length;
        var end = line.IndexOf(',', start);
        return long.TryParse(line[start..(end < 0 ? line.Length : end)], out var value) ? value : 0;
    }

    private static async Task UpdateAsync(string id, Func<DvrRecording, DvrRecording> update)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var account in await AppServices.Accounts.LoadAsync())
            foreach (var profile in UserProfileService.Profiles)
            {
                var items = await ReadAsync(account.Id, profile.Id);
                var index = items.FindIndex(item => item.Id == id);
                if (index < 0) continue;
                items[index] = update(items[index]);
                await WriteAsync(account.Id, profile.Id, items);
                Changed?.Invoke();
                return;
            }
        }
        finally { gate.Release(); }
    }

    private static Stream CreateRecordingFile(string fileName)
    {
#if ANDROID
        if (GetRecordingsTree() is { } tree)
        {
            tree.FindFile(fileName)?.Delete();
            var document = tree.CreateFile(MimeTypeFor(fileName), fileName)
                ?? throw new UnauthorizedAccessException("Não foi possível criar o ficheiro na pasta de gravações.");
            return Android.App.Application.Context.ContentResolver!.OpenOutputStream(document.Uri!, "w")
                ?? throw new UnauthorizedAccessException("Não foi possível escrever na pasta de gravações.");
        }
        if (UsesCustomRecordingsDirectory)
            throw new UnauthorizedAccessException("A autorização da pasta de gravações expirou. Escolha novamente a pasta.");
#endif
        Directory.CreateDirectory(RecordingsDirectory);
        return new FileStream(Path.Combine(RecordingsDirectory, Path.GetFileName(fileName)), FileMode.Create,
            FileAccess.Write, FileShare.Read, 128 * 1024, true);
    }

    private static long RecordingFileLength(string fileName)
    {
#if ANDROID
        if (GetRecordingsTree() is { } tree) return tree.FindFile(Path.GetFileName(fileName))?.Length() ?? 0;
#endif
        var path = Path.Combine(RecordingsDirectory, Path.GetFileName(fileName));
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static void DeleteFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;
        try
        {
#if ANDROID
            if (GetRecordingsTree() is { } tree)
            {
                tree.FindFile(Path.GetFileName(fileName))?.Delete();
                return;
            }
#endif
            var path = Path.GetFullPath(Path.Combine(RecordingsDirectory, Path.GetFileName(fileName)));
            if (path.StartsWith(Path.GetFullPath(RecordingsDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

#if ANDROID
    private static AndroidX.DocumentFile.Provider.DocumentFile? GetRecordingsTree() =>
        GetTree(Preferences.Default.Get(RecordingsTreeUriKey, ""));

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
            if (separator < 0 || !documentId[..separator].Equals("primary", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = documentId[(separator + 1)..].Replace('\\', '/').Trim('/');
            var path = "/storage/emulated/0" + (relative.Length == 0 ? "" : "/" + relative);
            if (string.Equals(path, normalized, StringComparison.Ordinal)) return uri.ToString();
        }
        return null;
    }

    private static IEnumerable<string> EnumerateRecordingFileNames(string directory,
        AndroidX.DocumentFile.Provider.DocumentFile? tree) => tree is not null
        ? (tree.ListFiles() ?? []).Where(file => file.IsFile).Select(file => file.Name ?? "").Where(name => name.Length > 0)
        : Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Select(path => Path.GetFileName(path)) : [];

    private static Stream OpenRecordingReadStream(string directory,
        AndroidX.DocumentFile.Provider.DocumentFile? tree, string fileName)
    {
        if (tree?.FindFile(fileName) is { } document)
            return Android.App.Application.Context.ContentResolver!.OpenInputStream(document.Uri!)
                ?? throw new IOException("Não foi possível ler uma gravação existente.");
        return File.OpenRead(Path.Combine(directory, fileName));
    }

    private static void DeleteAllFiles(string directory, AndroidX.DocumentFile.Provider.DocumentFile? tree)
    {
        if (tree is not null)
        {
            foreach (var file in (tree.ListFiles() ?? []).Where(file => file.IsFile)) file.Delete();
            return;
        }
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory))
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string MimeTypeFor(string fileName) =>
        fileName.Contains(".ts", StringComparison.OrdinalIgnoreCase) ? "video/mp2t" : "application/octet-stream";
#endif

    private static async Task<List<DvrRecording>> ReadAsync(string accountId, string profileId)
    {
        var json = await SecureStorage.Default.GetAsync(StorageKey(accountId, profileId));
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<DvrRecording>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Task WriteAsync(string accountId, string profileId, IEnumerable<DvrRecording> recordings) =>
        SecureStorage.Default.SetAsync(StorageKey(accountId, profileId), JsonSerializer.Serialize(recordings));

    private static async Task<List<DvrSeriesRule>> ReadRulesAsync(string accountId, string profileId)
    {
        var json = await SecureStorage.Default.GetAsync(RulesStorageKey(accountId, profileId));
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<DvrSeriesRule>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Task WriteRulesAsync(string accountId, string profileId, IEnumerable<DvrSeriesRule> rules) =>
        SecureStorage.Default.SetAsync(RulesStorageKey(accountId, profileId), JsonSerializer.Serialize(rules));

    private static string StorageKey(string accountId, string profileId) =>
        $"dvr-recordings.{UserProfileService.Scope(accountId, profileId)}";
    private static string RulesStorageKey(string accountId, string profileId) =>
        $"dvr-series-rules.{UserProfileService.Scope(accountId, profileId)}";
}

public static partial class DvrPlatformScheduler
{
    public static void Schedule(DvrRecording recording, DateTimeOffset triggerAt) => ScheduleCore(recording, triggerAt);
    public static void Cancel(string recordingId) => CancelCore(recordingId);
    static partial void ScheduleCore(DvrRecording recording, DateTimeOffset triggerAt);
    static partial void CancelCore(string recordingId);
}
