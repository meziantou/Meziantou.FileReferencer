using System.Text.RegularExpressions;
using Meziantou.FileReferencer;

namespace Meziantou.FileReferencer;
internal sealed record ReferenceMatch(string Reference, string Indentation)
{
    public bool? UpdateIndentation { get; set; }
    public bool? TrimFinalEmptyLines { get; set; }
    public EndOfLineOption? EndOfLine { get; set; }

    internal static ReferenceMatch Create(Match match)
    {
        var reference = match.Groups[Regexes.ReferenceGroupName].Value;
        var indentation = match.Groups[Regexes.IndentationGroupName].Value;
        var result = new ReferenceMatch(reference, indentation);
        var options = match.Groups["options"];
        var optionsName = match.Groups["name"];
        var optionsValue = match.Groups["value"];
        for (int i = 0; i < optionsName.Captures.Count; i++)
        {
            var name = optionsName.Captures[i].Value;
            var value = optionsValue.Captures[i].Value;

            if (name == "eol" && Enum.TryParse<EndOfLineOption>(value, ignoreCase: true, out var eol))
            {
                result.EndOfLine = eol;
            }
            else if (name == "indent" && bool.TryParse(value, out var indent))
            {
                result.UpdateIndentation = indent;
            }
            else if (name == "trim-final-lines" && bool.TryParse(value, out var trimEndLines))
            {
                result.TrimFinalEmptyLines = trimEndLines;
            }
        }

        return result;
    }
}
