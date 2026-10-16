using System.Text.Json;
using PSiptv.Core;

internal static class WindowsUpdateChecks
{
    public static void Run()
    {
        WindowsRelease Release(string version, string architecture = "x64", bool draft = false, bool prerelease = false,
            string? digest = null, string state = "uploaded") => new()
        {
            Tag = version, Draft = draft, Prerelease = prerelease,
            Assets = [new() { Name = $"PSiptv.v{version.TrimStart('v')}.windows-{architecture}.zip", Size = 123,
                Url = "https://api.github.com/repos/errors32/PSiptv/releases/assets/1", State = state,
                Digest = digest ?? "sha256:" + new string('a', 64) }]
        };
        void Check(bool valid, string label) { if (!valid) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
        var compatible = Release("v1.7.4");
        Check(WindowsRelease.Latest([Release("1.7.5", "arm64"), Release("1.8.0", draft: true),
            Release("1.9.0", prerelease: true), compatible], "1.7.3", "x64") == compatible,
            "Windows updates select a newer compatible stable package, ignoring drafts and other architectures");
        Check(WindowsRelease.Latest([compatible], "1.7.4", "x64") is null &&
            WindowsRelease.Latest([compatible], "1.7.5", "x64") is null, "Windows updates never downgrade or reinstall");
        Check(WindowsRelease.Latest([Release("1.7.9"), Release("1.7.10")], "1.7.3", "x64")?.Tag == "1.7.10",
            "Windows updates compare numeric versions");
        Check(Release("1.7.4", digest: "sha256:bad").Package("x64") is null &&
            Release("1.7.4", state: "new").Package("x64") is null, "Windows update packages require SHA-256 and completed upload");
        var parsed = JsonSerializer.Deserialize<WindowsRelease>("""
            {"tag_name":"1.7.4","assets":[{"name":"PSiptv.v1.7.4.windows-x64.zip","size":1,"state":"uploaded","url":"https://evil.example/file","digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}
            """);
        Check(parsed?.Package("x64") is null, "Windows updater refuses asset API URLs outside GitHub");
    }
}
