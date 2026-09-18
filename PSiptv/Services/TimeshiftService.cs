namespace PSiptv.Services;

public static class TimeshiftService
{
    public static string PrepareDirectory()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, "timeshift");
        Directory.CreateDirectory(path);
        Cleanup(path);
        return path;
    }

    public static void Cleanup()
    {
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, "timeshift");
            if (Directory.Exists(path)) Cleanup(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Cleanup(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-6)) File.Delete(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
