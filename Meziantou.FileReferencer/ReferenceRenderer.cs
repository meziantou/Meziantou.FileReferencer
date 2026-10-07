using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.FileReferencer;
internal static partial class ReferenceRenderer
{
    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bat"] = "batch",
        [".c"] = "c",
        [".cmd"] = "batch",
        [".cpp"] = "cpp",
        [".cs"] = "csharp",
        [".csproj"] = "xml",
        [".cshtml"] = "cshtml",
        [".css"] = "css",
        [".editorconfig"] = "ini",
        [".fs"] = "fsharp",
        [".fsproj"] = "xml",
        [".go"] = "go",
        [".h"] = "c",
        [".hpp"] = "cpp",
        [".htm"] = "html",
        [".html"] = "html",
        [".ini"] = "ini",
        [".java"] = "java",
        [".js"] = "javascript",
        [".json"] = "json",
        [".json5"] = "json5",
        [".jsx"] = "jsx",
        [".kt"] = "kotlin",
        [".less"] = "less",
        [".md"] = "markdown",
        [".props"] = "xml",
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".py"] = "python",
        [".razor"] = "razor",
        [".rb"] = "ruby",
        [".rs"] = "rust",
        [".scss"] = "scss",
        [".sh"] = "bash",
        [".sql"] = "sql",
        [".swift"] = "swift",
        [".targets"] = "xml",
        [".toml"] = "toml",
        [".ts"] = "typescript",
        [".tsx"] = "tsx",
        [".vb"] = "vbnet",
        [".vbproj"] = "xml",
        [".xaml"] = "xml",
        [".xml"] = "xml",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
    };

    [GeneratedRegex(@"^\s*#region(?:\s+(?<name>.*?))?\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex RegionStartRegex { get; }

    [GeneratedRegex(@"^\s*#endregion\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex RegionEndRegex { get; }

    /// <summary>
    /// Generates the content to insert between the reference markers.
    /// </summary>
    /// <param name="filePath">Path of the file containing the reference</param>
    /// <param name="match">The reference</param>
    /// <param name="markerEol">End of line of the start marker</param>
    /// <param name="defaultEol">End of line option to use when the reference does not specify one</param>
    /// <exception cref="ReferenceException">The reference is invalid</exception>
    public static async Task<string> RenderAsync(string filePath, ReferenceMatch match, string markerEol, EndOfLineOption defaultEol, CancellationToken cancellationToken)
    {
        if (match.Errors.Count > 0)
            throw new ReferenceException(string.Join(' ', match.Errors));

        if (markerEol.Length == 0)
        {
            markerEol = Environment.NewLine;
        }

        var content = await FileDownloader.DownloadFileAsync(filePath, match.Reference, cancellationToken);
        var sourceLines = new List<SourceLine>();
        foreach (var (line, eol) in content.SplitLines())
        {
            sourceLines.Add(new SourceLine(line, eol));
        }

        var (lines, linkRange) = SelectLines(sourceLines, match);
        if (match.Dedent)
        {
            lines = Dedent(lines);
        }

        if (match.TrimFinalEmptyLines ?? true)
        {
            while (lines.Count > 0 && lines[^1].Text.Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }
        }

        var eolOption = match.EndOfLine ?? defaultEol;
        var targetEol = eolOption switch
        {
            EndOfLineOption.Cr => "\r",
            EndOfLineOption.Lf => "\n",
            EndOfLineOption.CrLf => "\r\n",
            _ => markerEol,
        };

        var output = new List<SourceLine>(lines.Count + 4);
        string? fence = null;
        if (match.Format is ReferenceFormat.MarkdownFencedCodeBlock)
        {
            fence = new string('`', Math.Max(3, GetLongestBacktickRun(lines) + 1));
            var language = match.Language ?? InferLanguage(match.Reference);
            if (string.Equals(language, ReferenceMatch.LanguageNone, StringComparison.OrdinalIgnoreCase))
            {
                language = null;
            }

            output.Add(new SourceLine(fence + language, targetEol));
        }

        foreach (var line in lines)
        {
            var eol = eolOption is EndOfLineOption.AsIs && line.Eol.Length > 0 ? line.Eol : targetEol;
            output.Add(line with { Eol = eol });
        }

        if (fence is not null)
        {
            output.Add(new SourceLine(fence, targetEol));
        }

        if (match.SourceLink is not null)
        {
            var url = match.SourceLink == ReferenceMatch.SourceLinkAuto
                ? await SourceLinkResolver.ResolveAsync(filePath, match.Reference, linkRange, cancellationToken)
                : match.SourceLink;

            output.Add(new SourceLine("", targetEol));
            output.Add(new SourceLine($"[source code]({url})", targetEol));
        }

        // Match indentation (e.g. json, yaml, markdown lists)
        var updateIndentation = (match.UpdateIndentation ?? true) && !string.IsNullOrEmpty(match.Indentation);
        var result = new StringBuilder();
        foreach (var line in output)
        {
            if (updateIndentation)
            {
                if (!string.IsNullOrWhiteSpace(line.Text))
                {
                    result.Append(match.Indentation).Append(line.Text);
                }
            }
            else
            {
                result.Append(line.Text);
            }

            result.Append(line.Eol);
        }

        return result.ToString();
    }

    private static (List<SourceLine> Lines, LineRange? LinkRange) SelectLines(List<SourceLine> lines, ReferenceMatch match)
    {
        if (match.Lines is { } range)
        {
            if (range.End > lines.Count)
                throw new ReferenceException(string.Create(CultureInfo.InvariantCulture, $"The line range '{range}' is out of bounds: '{match.Reference}' contains {lines.Count} line(s)."));

            return (lines.GetRange(range.Start - 1, range.End - range.Start + 1), range);
        }

        if (match.Region is { } regionName)
        {
            var startIndex = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                var regionMatch = RegionStartRegex.Match(lines[i].Text);
                if (regionMatch.Success && regionMatch.Groups["name"].Value == regionName)
                {
                    if (startIndex >= 0)
                        throw new ReferenceException(string.Create(CultureInfo.InvariantCulture, $"The region '{regionName}' is ambiguous: it is defined multiple times in '{match.Reference}' (lines {startIndex + 1} and {i + 1})."));

                    startIndex = i;
                }
            }

            if (startIndex < 0)
                throw new ReferenceException($"The region '{regionName}' was not found in '{match.Reference}'.");

            var depth = 1;
            for (var i = startIndex + 1; i < lines.Count; i++)
            {
                if (RegionStartRegex.IsMatch(lines[i].Text))
                {
                    depth++;
                }
                else if (RegionEndRegex.IsMatch(lines[i].Text))
                {
                    depth--;
                    if (depth == 0)
                    {
                        // Line numbers are 1-based and exclude the region delimiters
                        LineRange? linkRange = i - startIndex > 1 ? new LineRange(startIndex + 2, i) : null;
                        return (lines.GetRange(startIndex + 1, i - startIndex - 1), linkRange);
                    }
                }
            }

            throw new ReferenceException(string.Create(CultureInfo.InvariantCulture, $"The region '{regionName}' (line {startIndex + 1}) is not closed in '{match.Reference}'."));
        }

        return (lines, null);
    }

    private static List<SourceLine> Dedent(List<SourceLine> lines)
    {
        string? commonIndentation = null;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;

            var indentation = line.Text[..(line.Text.Length - line.Text.AsSpan().TrimStart().Length)];
            if (commonIndentation is null)
            {
                commonIndentation = indentation;
            }
            else
            {
                var length = commonIndentation.AsSpan().CommonPrefixLength(indentation);
                commonIndentation = commonIndentation[..length];
            }

            if (commonIndentation.Length == 0)
                return lines;
        }

        if (string.IsNullOrEmpty(commonIndentation))
            return lines;

        var result = new List<SourceLine>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Text.StartsWith(commonIndentation, StringComparison.Ordinal))
            {
                result.Add(line with { Text = line.Text[commonIndentation.Length..] });
            }
            else
            {
                // Whitespace-only line shorter than the common indentation
                result.Add(line with { Text = "" });
            }
        }

        return result;
    }

    private static int GetLongestBacktickRun(List<SourceLine> lines)
    {
        var max = 0;
        foreach (var line in lines)
        {
            var current = 0;
            foreach (var c in line.Text)
            {
                if (c == '`')
                {
                    current++;
                    max = Math.Max(max, current);
                }
                else
                {
                    current = 0;
                }
            }
        }

        return max;
    }

    private static string? InferLanguage(string reference)
    {
        var path = FileDownloader.TryGetRemoteUri(reference, out var uri) ? uri.AbsolutePath : reference;
        var fileName = Path.GetFileName(path);
        if (string.Equals(fileName, "dockerfile", StringComparison.OrdinalIgnoreCase))
            return "dockerfile";

        var extension = Path.GetExtension(fileName);
        return LanguageByExtension.TryGetValue(extension, out var language) ? language : null;
    }

    private readonly record struct SourceLine(string Text, string Eol);
}
