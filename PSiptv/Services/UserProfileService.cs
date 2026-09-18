using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public sealed record UserProfile(string Id, string Name, string Avatar);

public static class UserProfileService
{
    private const string ProfilesKey = "psiptv.user-profiles.v1";
    private const string ActiveProfileKey = "activeUserProfile";
    private static readonly SemaphoreSlim gate = new(1, 1);
    public static UserProfile DefaultProfile { get; private set; } =
        new(ProfileStorageScope.DefaultProfileId, "Principal", "👤");
    public static IReadOnlyList<string> AvatarChoices { get; } =
        ["👤", "🙂", "😎", "🎬", "📺", "⭐", "🎧", "🚀", "⚽", "🎮", "🐶", "🐱"];
    private static IReadOnlyList<UserProfile> profiles = [DefaultProfile];
    private static bool loaded;

    public static IReadOnlyList<UserProfile> Profiles => profiles;
    public static UserProfile Active { get; private set; } = DefaultProfile;

    public static string Scope(string accountId, string? profileId = null) =>
        ProfileStorageScope.ForAccount(accountId, profileId ?? Active.Id);

    public static async Task LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (loaded) return;
            IReadOnlyList<UserProfile> saved = [];
            try
            {
                var json = await SecureStorage.Default.GetAsync(ProfilesKey);
                if (!string.IsNullOrWhiteSpace(json))
                    saved = JsonSerializer.Deserialize<List<UserProfile>>(json) ?? [];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            var savedDefault = saved.FirstOrDefault(profile => profile.Id == ProfileStorageScope.DefaultProfileId);
            if (savedDefault is not null && !string.IsNullOrWhiteSpace(savedDefault.Name))
                DefaultProfile = DefaultProfile with
                {
                    Name = savedDefault.Name.Trim()[..Math.Min(savedDefault.Name.Trim().Length, 30)],
                    Avatar = ValidAvatar(savedDefault.Avatar)
                };
            profiles = new[] { DefaultProfile }
                .Concat(saved.Where(profile => profile is not null && profile.Id != DefaultProfile.Id &&
                    !string.IsNullOrWhiteSpace(profile.Id) && !string.IsNullOrWhiteSpace(profile.Name))
                    .Select(profile => profile with
                    {
                    Name = profile.Name.Trim(),
                    Avatar = ValidAvatar(profile.Avatar)
                    }))
                .DistinctBy(profile => profile.Id)
                .Take(8)
                .ToArray();
            var activeId = Preferences.Default.Get(ActiveProfileKey, DefaultProfile.Id);
            Active = profiles.FirstOrDefault(profile => profile.Id == activeId) ?? DefaultProfile;
            Preferences.Default.Set(ActiveProfileKey, Active.Id);
            loaded = true;
        }
        finally { gate.Release(); }
    }

    public static async Task<UserProfile> CreateAsync(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 30)
            throw new InvalidOperationException("O nome do perfil deve ter entre 1 e 30 caracteres.");
        await LoadAsync();
        await gate.WaitAsync();
        try
        {
            if (profiles.Count >= 8) throw new InvalidOperationException("Pode criar até 8 perfis.");
            if (profiles.Any(profile => profile.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException("Já existe um perfil com esse nome.");
            var profile = new UserProfile(Guid.NewGuid().ToString("N"), name,
                AvatarChoices[(profiles.Count - 1) % AvatarChoices.Count]);
            profiles = profiles.Append(profile).ToArray();
            await SaveAsync();
            return profile;
        }
        finally { gate.Release(); }
    }

    public static void Activate(string id)
    {
        var selected = profiles.FirstOrDefault(profile => profile.Id == id)
            ?? throw new InvalidOperationException("O perfil selecionado já não existe.");
        Active = selected;
        Preferences.Default.Set(ActiveProfileKey, selected.Id);
    }

    public static async Task<UserProfile> UpdateAsync(string id, string name, string avatar)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 30)
            throw new InvalidOperationException("O nome do perfil deve ter entre 1 e 30 caracteres.");
        await LoadAsync();
        await gate.WaitAsync();
        try
        {
            var index = profiles.ToList().FindIndex(profile => profile.Id == id);
            if (index < 0) throw new InvalidOperationException("O perfil selecionado já não existe.");
            if (profiles.Any(profile => profile.Id != id &&
                profile.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException("Já existe um perfil com esse nome.");
            var updated = profiles[index] with { Name = name, Avatar = ValidAvatar(avatar) };
            var copy = profiles.ToArray();
            copy[index] = updated;
            profiles = copy;
            if (id == DefaultProfile.Id) DefaultProfile = updated;
            if (Active.Id == id) Active = updated;
            await SaveAsync();
            return updated;
        }
        finally { gate.Release(); }
    }

    public static async Task DeleteAsync(string id)
    {
        if (id == DefaultProfile.Id) throw new InvalidOperationException("O perfil Principal não pode ser eliminado.");
        await LoadAsync();
        await gate.WaitAsync();
        try
        {
            if (Active.Id == id) throw new InvalidOperationException("Mude de perfil antes de o eliminar.");
            profiles = profiles.Where(profile => profile.Id != id).ToArray();
            await SaveAsync();
        }
        finally { gate.Release(); }
    }

    public static async Task MergeAsync(IEnumerable<UserProfile> imported)
    {
        await LoadAsync();
        await gate.WaitAsync();
        try
        {
            var importedProfiles = imported.Where(profile => profile is not null).ToArray();
            var importedDefault = importedProfiles.LastOrDefault(profile => profile.Id == DefaultProfile.Id);
            if (importedDefault is not null && !string.IsNullOrWhiteSpace(importedDefault.Name))
                DefaultProfile = DefaultProfile with
                {
                    Name = importedDefault.Name.Trim()[..Math.Min(importedDefault.Name.Trim().Length, 30)],
                    Avatar = ValidAvatar(importedDefault.Avatar)
                };
            if (Active.Id == DefaultProfile.Id) Active = DefaultProfile;
            var candidates = importedProfiles.Where(profile =>
                profile.Id != DefaultProfile.Id && !string.IsNullOrWhiteSpace(profile.Id) &&
                !string.IsNullOrWhiteSpace(profile.Name));
            profiles = new[] { DefaultProfile }.Concat(profiles.Where(profile => profile.Id != DefaultProfile.Id))
                .Concat(candidates)
                .GroupBy(profile => profile.Id)
                .Select(group => group.Last())
                .Take(8)
                .ToArray();
            await SaveAsync();
        }
        finally { gate.Release(); }
    }

    private static string ValidAvatar(string? avatar) =>
        AvatarChoices.Contains(avatar ?? "") ? avatar! : "👤";

    private static Task SaveAsync() => SecureStorage.Default.SetAsync(ProfilesKey,
        JsonSerializer.Serialize(profiles));
}
