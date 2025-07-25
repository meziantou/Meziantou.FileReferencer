using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Meziantou.FileReferencer;
internal partial class CSharpParser : Parser
{
    [GeneratedRegex($"^#region(\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex CSharpRegionRegexStart { get; }

    private Regex? _endRegex;
    private int regionCount;

    public override ReferenceMatch? MatchStart(string line)
    {
        var match = Regexes.CSharpRegionRegexStart.Match(line);
        if (match.Success)
        {
            regionCount++;
            _endRegex = Regexes.CSharpRegionRegexEnd;
            return ReferenceMatch.Create(match);
        }

        match = Regexes.DoubleSlashRegexStart.Match(line);
        if (match.Success)
        {
            regionCount = 0;
            _endRegex = Regexes.DoubleSlashRegexEnd;
            return ReferenceMatch.Create(match);
        }

        return null;
    }

    public override bool MatchEnd(string line)
    {
        Debug.Assert(_endRegex is not null);
        if (regionCount > 0)
        {
            if (CSharpRegionRegexStart.IsMatch(line))
            {
                // If we are in a region, we don't match the end of the region
                regionCount++;
                return false;
            }
        }

        var match = _endRegex.Match(line);
        if (match.Success)
        {
            regionCount--;

            if (regionCount <= 0)
            {
                _endRegex = null;
                return true;
            }
        }

        return false;
    }
}
