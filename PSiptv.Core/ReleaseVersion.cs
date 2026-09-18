namespace PSiptv.Core;

/// <summary>Version precedence used by application release tags.</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly int[] numbers;
    private readonly string[] preRelease;

    private ReleaseVersion(int[] numbers, string[] preRelease)
    {
        this.numbers = numbers;
        this.preRelease = preRelease;
    }

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = null!;
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        var metadata = text.IndexOf('+');
        if (metadata >= 0) text = text[..metadata];
        var separator = text.IndexOf('-');
        var numericText = separator < 0 ? text : text[..separator];
        var labels = separator < 0 ? [] : text[(separator + 1)..].Split('.');
        if (labels.Any(label => label.Length == 0 || label.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-')))
            return false;

        var components = numericText.Split('.');
        if (components.Length is < 1 or > 4) return false;
        var parsed = new int[Math.Max(3, components.Length)];
        for (var index = 0; index < components.Length; index++)
            if (!int.TryParse(components[index], out parsed[index]) || parsed[index] < 0)
                return false;

        version = new ReleaseVersion(parsed, labels);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        for (var index = 0; index < Math.Max(numbers.Length, other.numbers.Length); index++)
        {
            var comparison = (index < numbers.Length ? numbers[index] : 0)
                .CompareTo(index < other.numbers.Length ? other.numbers[index] : 0);
            if (comparison != 0) return comparison;
        }

        if (preRelease.Length == 0) return other.preRelease.Length == 0 ? 0 : 1;
        if (other.preRelease.Length == 0) return -1;
        for (var index = 0; index < Math.Min(preRelease.Length, other.preRelease.Length); index++)
        {
            var leftNumeric = int.TryParse(preRelease[index], out var leftNumber);
            var rightNumeric = int.TryParse(other.preRelease[index], out var rightNumber);
            var comparison = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric ? -1
                : rightNumeric ? 1
                : string.Compare(preRelease[index], other.preRelease[index], StringComparison.Ordinal);
            if (comparison != 0) return comparison;
        }
        return preRelease.Length.CompareTo(other.preRelease.Length);
    }
}
