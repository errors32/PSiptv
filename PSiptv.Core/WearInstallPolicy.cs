using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace PSiptv.Core;

public static partial class WearInstallPolicy
{
    public const string PackageName = "com.PS.PSiptv";
    public const long MaximumApkBytes = 128 * 1024 * 1024;

    public static string AssetName(string tag)
    {
        if (!StableTag().IsMatch(tag.Trim()) || !ReleaseVersion.TryParse(tag, out _)) throw new ArgumentException("A versão publicada é inválida.");
        return $"PSiptv.Remote.Wear.v{tag.Trim().TrimStart('v', 'V')}.apk";
    }

    public static string Host(string value)
    {
        if (!IPAddress.TryParse(value.Trim(), out var address))
            throw new ArgumentException("Introduza o endereço IP do relógio, sem a porta.");
        var bytes = address.GetAddressBytes();
        var local = bytes.Length == 4
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 169 && bytes[1] == 254
            : address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc;
        if (!local) throw new ArgumentException("Use o IP do relógio na mesma rede Wi-Fi do telemóvel.");
        return address.ToString();
    }

    public static int Port(string value) => int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
        ? port : throw new ArgumentException("Introduza uma porta entre 1 e 65535, apresentada no relógio.");

    public static string PairingCode(string value) => value.Trim() is { Length: 6 } code && code.All(c => c is >= '0' and <= '9')
        ? code : throw new ArgumentException("Introduza o código de emparelhamento de seis algarismos do relógio.");

    public static bool ValidDigest(string? digest) => digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && digest[7..].All(Uri.IsHexDigit);

    public static bool IsWatch(string features) => features.Split('\n').Any(line => line.Trim() == "feature:android.hardware.type.watch");

    public static (long Code, string Name)? InstalledVersion(string packageDump)
    {
        var code = VersionCode().Match(packageDump);
        var name = VersionName().Match(packageDump);
        return code.Success && long.TryParse(code.Groups[1].Value, out var value)
            ? (value, name.Success ? name.Groups[1].Value.Trim() : "") : null;
    }

    public static bool InstallSucceeded(string output) => output.Split('\n').Any(line => line.Trim() == "Success") &&
        !output.Contains("Failure", StringComparison.OrdinalIgnoreCase) && !output.Contains("Error:", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\bversionCode=(\d+)\b")]
    private static partial Regex VersionCode();
    [GeneratedRegex(@"\bversionName=([^\r\n]+)")]
    private static partial Regex VersionName();
    [GeneratedRegex(@"^[vV]?[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$")]
    private static partial Regex StableTag();
}
