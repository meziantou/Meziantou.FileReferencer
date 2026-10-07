using System.Text.RegularExpressions;

namespace Meziantou.FileReferencer;
internal sealed record ReferenceMatch(string Reference, string Indentation)
{
    public const string SourceLinkAuto = "auto";
    public const string LanguageNone = "none";

    public bool? UpdateIndentation { get; set; }
    public bool? TrimFinalEmptyLines { get; set; }
    public EndOfLineOption? EndOfLine { get; set; }
    public ReferenceFormat Format { get; set; }
    public string? Language { get; set; }
    public LineRange? Lines { get; set; }
    public string? Region { get; set; }
    public bool Dedent { get; set; }
    public string? SourceLink { get; set; }
    public List<string> Errors { get; } = [];

    internal static ReferenceMatch Create(Match match)
    {
        var reference = match.Groups[Regexes.ReferenceGroupName].Value;
        var indentation = match.Groups[Regexes.IndentationGroupName].Value;
        var result = new ReferenceMatch(reference, indentation);
        var optionsName = match.Groups["name"];
        var optionsValue = match.Groups["value"];
        for (int i = 0; i < optionsName.Captures.Count; i++)
        {
            var name = optionsName.Captures[i].Value.Trim();
            var value = optionsValue.Captures[i].Value.Trim();

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
            else if (name == "format")
            {
                if (value == "md-fenced-code-block")
                {
                    result.Format = ReferenceFormat.MarkdownFencedCodeBlock;
                }
                else
                {
                    result.Errors.Add($"Unknown format '{value}'. Supported values: md-fenced-code-block.");
                }
            }
            else if (name == "language")
            {
                if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || c == '`'))
                {
                    result.Errors.Add($"Invalid language '{value}'. The language must be a non-empty identifier without whitespace or backticks.");
                }
                else
                {
                    result.Language = value;
                }
            }
            else if (name == "lines")
            {
                if (LineRange.TryParse(value, out var range))
                {
                    result.Lines = range;
                }
                else
                {
                    result.Errors.Add($"Invalid line range '{value}'. Expected 'start-end' or 'line' with 1-based line numbers.");
                }
            }
            else if (name == "region")
            {
                if (value.Length == 0)
                {
                    result.Errors.Add("The region name cannot be empty.");
                }
                else
                {
                    result.Region = value;
                }
            }
            else if (name == "dedent")
            {
                if (bool.TryParse(value, out var dedent))
                {
                    result.Dedent = dedent;
                }
                else
                {
                    result.Errors.Add($"Invalid dedent value '{value}'. Expected 'true' or 'false'.");
                }
            }
            else if (name == "source-link")
            {
                if (string.Equals(value, SourceLinkAuto, StringComparison.OrdinalIgnoreCase))
                {
                    result.SourceLink = SourceLinkAuto;
                }
                else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    result.SourceLink = value;
                }
                else
                {
                    result.Errors.Add($"Invalid source-link '{value}'. Expected 'auto' or an absolute http(s) URL.");
                }
            }
        }

        if (result.Lines is not null && result.Region is not null)
        {
            result.Errors.Add("The 'lines' and 'region' options are mutually exclusive.");
        }

        return result;
    }
}
