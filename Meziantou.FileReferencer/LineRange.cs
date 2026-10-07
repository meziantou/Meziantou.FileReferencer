using System.Globalization;

namespace Meziantou.FileReferencer;

/// <summary>Inclusive, 1-based range of lines.</summary>
internal readonly record struct LineRange(int Start, int End)
{
    public static bool TryParse(string value, out LineRange range)
    {
        range = default;
        var separatorIndex = value.IndexOf('-', StringComparison.Ordinal);
        var startText = separatorIndex < 0 ? value : value[..separatorIndex];
        var endText = separatorIndex < 0 ? value : value[(separatorIndex + 1)..];

        if (!int.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out var end))
        {
            return false;
        }

        if (start < 1 || end < start)
            return false;

        range = new LineRange(start, end);
        return true;
    }

    public override string ToString() => Start == End ? Start.ToString(CultureInfo.InvariantCulture) : $"{Start.ToString(CultureInfo.InvariantCulture)}-{End.ToString(CultureInfo.InvariantCulture)}";
}
