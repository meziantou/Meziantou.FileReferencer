using System.Text.RegularExpressions;

namespace Meziantou.FileReferencer;
class RegexParser(Regex startRegex, Regex endRegex) : Parser
{
    public override ReferenceMatch? MatchStart(string line)
    {
        var match = startRegex.Match(line);
        if (match.Success)
            return ReferenceMatch.Create(match);

        return null;
    }

    public override bool MatchEnd(string line) => endRegex.IsMatch(line);
}
