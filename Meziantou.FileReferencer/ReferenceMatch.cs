using System.Text.RegularExpressions;

namespace Meziantou.FileReferencer;
internal sealed record ReferenceMatch(string Reference, string Indentation)
{
    public const string SourceLinkAuto = "auto";
    public const string LanguageNone = "none";
    public const string MarkdownFencedCodeBlockFormatName = "md-fenced-code-block";
    public const string XmlDocCodeFormatName = "xmldoc-code";

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
            result.ApplyOption(name, value, strict: false);
        }

        if (result.Format is ReferenceFormat.XmlDocCode)
        {
            result.Errors.Add($"The '{XmlDocCodeFormatName}' format is only supported in C# XML documentation comments.");
        }

        result.Validate();
        return result;
    }

    /// <summary>Applies an option of the reference.</summary>
    /// <param name="strict">When <see langword="true"/>, unknown options and invalid values of the <c>eol</c>, <c>indent</c>, and <c>trim-final-lines</c> options are reported as errors instead of being ignored.</param>
    internal void ApplyOption(string name, string value, bool strict)
    {
        if (name == "eol")
        {
            if (Enum.TryParse<EndOfLineOption>(value, ignoreCase: true, out var eol) && (!strict || Enum.GetNames<EndOfLineOption>().Contains(value, StringComparer.OrdinalIgnoreCase)))
            {
                EndOfLine = eol;
            }
            else if (strict)
            {
                Errors.Add($"Invalid eol value '{value}'. Expected 'asis', 'auto', 'cr', 'lf', or 'crlf'.");
            }
        }
        else if (name == "indent")
        {
            if (bool.TryParse(value, out var indent))
            {
                UpdateIndentation = indent;
            }
            else if (strict)
            {
                Errors.Add($"Invalid indent value '{value}'. Expected 'true' or 'false'.");
            }
        }
        else if (name == "trim-final-lines")
        {
            if (bool.TryParse(value, out var trimEndLines))
            {
                TrimFinalEmptyLines = trimEndLines;
            }
            else if (strict)
            {
                Errors.Add($"Invalid trim-final-lines value '{value}'. Expected 'true' or 'false'.");
            }
        }
        else if (name == "format")
        {
            if (value == MarkdownFencedCodeBlockFormatName)
            {
                Format = ReferenceFormat.MarkdownFencedCodeBlock;
            }
            else if (value == XmlDocCodeFormatName)
            {
                Format = ReferenceFormat.XmlDocCode;
            }
            else
            {
                Errors.Add($"Unknown format '{value}'. Supported values: {MarkdownFencedCodeBlockFormatName}, {XmlDocCodeFormatName} (C# XML documentation comments only).");
            }
        }
        else if (name == "language")
        {
            if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || c == '`'))
            {
                Errors.Add($"Invalid language '{value}'. The language must be a non-empty identifier without whitespace or backticks.");
            }
            else
            {
                Language = value;
            }
        }
        else if (name == "lines")
        {
            if (LineRange.TryParse(value, out var range))
            {
                Lines = range;
            }
            else
            {
                Errors.Add($"Invalid line range '{value}'. Expected 'start-end' or 'line' with 1-based line numbers.");
            }
        }
        else if (name == "region")
        {
            if (value.Length == 0)
            {
                Errors.Add("The region name cannot be empty.");
            }
            else
            {
                Region = value;
            }
        }
        else if (name == "dedent")
        {
            if (bool.TryParse(value, out var dedent))
            {
                Dedent = dedent;
            }
            else
            {
                Errors.Add($"Invalid dedent value '{value}'. Expected 'true' or 'false'.");
            }
        }
        else if (name == "source-link")
        {
            if (string.Equals(value, SourceLinkAuto, StringComparison.OrdinalIgnoreCase))
            {
                SourceLink = SourceLinkAuto;
            }
            else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                SourceLink = value;
            }
            else
            {
                Errors.Add($"Invalid source-link '{value}'. Expected 'auto' or an absolute http(s) URL.");
            }
        }
        else if (strict)
        {
            Errors.Add($"Unknown option '{name}'.");
        }
    }

    /// <summary>Validates the combination of options.</summary>
    internal void Validate()
    {
        if (Lines is not null && Region is not null)
        {
            Errors.Add("The 'lines' and 'region' options are mutually exclusive.");
        }
    }
}
