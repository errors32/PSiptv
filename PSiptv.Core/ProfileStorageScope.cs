namespace PSiptv.Core;

public static class ProfileStorageScope
{
    public const string DefaultProfileId = "default";

    public static string ForAccount(string accountId, string profileId) =>
        profileId == DefaultProfileId ? accountId : $"{accountId}.profile.{profileId}";
}
