namespace PSiptv.Core;

public static class ExternalPlaylist
{
    public static string Create(MediaItem item)
    {
        var uri = WebAddress.Require(item.Url);
        var title = item.Name.Replace('\r', ' ').Replace('\n', ' ');
        // Never let provider metadata inject additional M3U records.
        return $"#EXTM3U\n#EXTINF:-1,{title}\n{uri.AbsoluteUri}\n";
    }
}
