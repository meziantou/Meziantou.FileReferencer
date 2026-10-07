using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Meziantou.FileReferencer;

/// <summary>
/// Updates the content of the elements of C# XML documentation comments (<c>///</c>) that have a <c>source</c> attribute in the <see cref="NamespaceUri"/> namespace.
/// </summary>
/// <example>
/// <code>
/// /// &lt;code xmlns:ref="urn:meziantou:file-referencer" ref:source="../samples/Example.cs" ref:region="BasicUsage" ref:dedent="true"&gt;
/// /// &lt;/code&gt;
/// </code>
/// </example>
internal static partial class XmlDocReferenceUpdater
{
    public const string NamespaceUri = "urn:meziantou:file-referencer";
    private const string SourceAttributeName = "source";

    // Attributes supported in the referencer namespace, in addition to "source". They share the semantics of the options of comment markers.
    private static readonly HashSet<string> SupportedOptions = new(StringComparer.Ordinal)
    {
        "dedent",
        "eol",
        "format",
        "indent",
        "lines",
        "region",
        "source-link",
        "trim-final-lines",
    };

    // "////" is a regular comment, not a documentation comment
    [GeneratedRegex(@"^(?<prefix>\s*///)(?!/)(?<content>.*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex DocCommentLineRegex { get; }

    public static bool IsSupported(string filePath) => string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>Updates all the references contained in the documentation comments of a C# file.</summary>
    /// <param name="filePath">Path of the file containing the references</param>
    /// <param name="content">Content of the file</param>
    /// <param name="defaultEol">End of line option to use when the reference does not specify one</param>
    public static async Task<UpdateResult> UpdateAsync(string filePath, string content, EndOfLineOption defaultEol, CancellationToken cancellationToken)
    {
        var result = new StringBuilder(content.Length);
        var block = new List<DocLine>();
        var blockLineNumber = 0;
        var lineNumber = 0;
        var referenceCount = 0;
        var hasErrors = false;
        foreach (var (line, eol) in content.SplitLines())
        {
            lineNumber++;
            var match = DocCommentLineRegex.Match(line);
            if (match.Success)
            {
                if (block.Count == 0)
                {
                    blockLineNumber = lineNumber;
                }

                block.Add(new DocLine(match.Groups["prefix"].Value, match.Groups["content"].Value, eol));
                continue;
            }

            await FlushBlockAsync();
            result.Append(line).Append(eol);
        }

        await FlushBlockAsync();
        return new UpdateResult(result.ToString(), referenceCount, hasErrors);

        async Task FlushBlockAsync()
        {
            if (block.Count == 0)
                return;

            var blockResult = await UpdateBlockAsync(filePath, block, blockLineNumber, defaultEol, result, cancellationToken);
            referenceCount += blockResult.ReferenceCount;
            hasErrors |= blockResult.HasErrors;
            block.Clear();
        }
    }

    private static async Task<(int ReferenceCount, bool HasErrors)> UpdateBlockAsync(string filePath, List<DocLine> lines, int firstLineNumber, EndOfLineOption defaultEol, StringBuilder result, CancellationToken cancellationToken)
    {
        var document = new DocComment(lines);

        // Most documentation comments do not contain references. Avoid parsing them, so malformed comments unrelated to this tool are not reported.
        if (!document.Xml.Contains(NamespaceUri, StringComparison.Ordinal))
        {
            document.AppendTo(result, []);
            return (0, false);
        }

        List<Target> targets;
        try
        {
            targets = FindTargets(document);
        }
        catch (XmlException ex)
        {
            Console.Error.WriteLine($"Error in file {filePath}: cannot parse the XML documentation comment at line {firstLineNumber + ex.LineNumber - 1}: {ex.Message}");
            document.AppendTo(result, []);
            return (0, true);
        }

        var hasErrors = false;
        var replacements = new List<Replacement>(targets.Count);
        foreach (var target in targets)
        {
            var lineNumber = firstLineNumber + target.Line;
            Console.WriteLine($"Found XML documentation reference: {target.Match.Reference} in file {filePath} at line {lineNumber.ToString(CultureInfo.InvariantCulture)}");
            try
            {
                replacements.Add(await RenderAsync(filePath, document, target, defaultEol, cancellationToken));
            }
            catch (ReferenceException ex)
            {
                // Keep the existing content of the target element
                Console.Error.WriteLine($"Error in file {filePath}: cannot update reference {target.Match.Reference} (line {lineNumber.ToString(CultureInfo.InvariantCulture)}): {ex.Message}");
                hasErrors = true;
            }
        }

        document.AppendTo(result, replacements);
        return (targets.Count, hasErrors);
    }

    /// <exception cref="XmlException">The documentation comment is not valid XML</exception>
    private static List<Target> FindTargets(DocComment document)
    {
        var settings = new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        var xml = document.Xml;
        var targets = new List<Target>();
        var openElements = new Stack<PendingTarget?>();
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        var lineInfo = (IXmlLineInfo)reader;
        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.Element)
            {
                // The line position of an element is the position of its name, just after '<'
                var startTagStart = document.GetOffset(lineInfo.LineNumber - 1, lineInfo.LinePosition - 1) - 1;
                var isEmptyElement = reader.IsEmptyElement;
                var match = CreateMatch(reader);

                PendingTarget? target = null;
                if (match is not null)
                {
                    var outerTarget = openElements.FirstOrDefault(item => item is not null);
                    if (outerTarget is not null)
                    {
                        // The content of the outer target is replaced, so the nested target would be removed
                        outerTarget.Match.Errors.Add($"The element contains a nested reference ('{match.Reference}'). References cannot be nested.");
                    }
                    else
                    {
                        var startTagEnd = GetStartTagEnd(xml, startTagStart);
                        target = new PendingTarget(match, reader.Name, startTagStart, startTagEnd, document.GetLineIndex(startTagStart));
                    }
                }

                if (isEmptyElement)
                {
                    if (target is not null)
                    {
                        targets.Add(target.ToTarget(endTagStart: null));
                    }
                }
                else
                {
                    openElements.Push(target);
                }
            }
            else if (reader.NodeType is XmlNodeType.EndElement)
            {
                var target = openElements.Pop();
                if (target is not null)
                {
                    // The line position of an end element is the position of its name, just after '</'
                    var endTagStart = document.GetOffset(lineInfo.LineNumber - 1, lineInfo.LinePosition - 1) - 2;
                    targets.Add(target.ToTarget(endTagStart));
                }
            }
        }

        return targets;
    }

    /// <summary>Creates the reference from the attributes of the current element.</summary>
    /// <returns><see langword="null"/> if the element does not have any attribute in the referencer namespace</returns>
    private static ReferenceMatch? CreateMatch(XmlReader reader)
    {
        string? source = null;
        var options = new List<(string Name, string Value)>();
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                if (reader.NamespaceURI == NamespaceUri)
                {
                    if (reader.LocalName == SourceAttributeName)
                    {
                        source = reader.Value;
                    }
                    else
                    {
                        options.Add((reader.LocalName, reader.Value));
                    }
                }
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        if (source is null && options.Count == 0)
            return null;

        var match = new ReferenceMatch(source?.Trim() ?? "", Indentation: "");
        if (source is null)
        {
            match.Errors.Add($"The '{SourceAttributeName}' attribute is required on the <{reader.Name}> element.");
        }
        else if (match.Reference.Length == 0)
        {
            match.Errors.Add($"The '{SourceAttributeName}' attribute cannot be empty.");
        }

        foreach (var (name, value) in options)
        {
            if (SupportedOptions.Contains(name))
            {
                match.ApplyOption(name, value.Trim(), strict: true);
            }
            else
            {
                match.Errors.Add($"Unknown attribute '{name}'. Supported attributes: {SourceAttributeName}, {string.Join(", ", SupportedOptions)}.");
            }
        }

        if (match.Format is ReferenceFormat.MarkdownFencedCodeBlock)
        {
            match.Errors.Add($"The '{ReferenceMatch.MarkdownFencedCodeBlockFormatName}' format is not supported in XML documentation comments. Supported values: {ReferenceMatch.XmlDocCodeFormatName}.");
        }

        if (match.SourceLink is not null && match.Format is not ReferenceFormat.XmlDocCode)
        {
            match.Errors.Add($"The 'source-link' attribute requires format=\"{ReferenceMatch.XmlDocCodeFormatName}\", so the link is generated next to the code instead of inside it.");
        }

        match.Validate();
        return match;
    }

    /// <summary>Gets the offset just after the '&gt;' character that ends the start tag starting at <paramref name="startTagStart"/>.</summary>
    private static int GetStartTagEnd(string xml, int startTagStart)
    {
        // The XML is well-formed, so '>' can only appear in quoted attribute values
        char? quote = null;
        for (var i = startTagStart + 1; i < xml.Length; i++)
        {
            var c = xml[i];
            if (quote is not null)
            {
                if (c == quote)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i + 1;
            }
        }

        throw new InvalidOperationException("The end of the start tag was not found.");
    }

    private static async Task<Replacement> RenderAsync(string filePath, DocComment document, Target target, EndOfLineOption defaultEol, CancellationToken cancellationToken)
    {
        var match = target.Match;
        var (lines, linkRange) = await ReferenceRenderer.GetContentAsync(filePath, match, cancellationToken);

        var startLine = document.Lines[target.Line];
        var markerEol = startLine.Eol.Length > 0 ? startLine.Eol : Environment.NewLine;
        var eolOption = match.EndOfLine ?? defaultEol;
        var targetEol = ReferenceRenderer.GetEndOfLine(eolOption, markerEol);

        // Generated lines are aligned with the line that contains the start tag of the target element.
        // The "///" prefix is always preserved, whatever the indentation option.
        var indentation = "";
        if (match.UpdateIndentation ?? true)
        {
            var lineContent = startLine.Content;
            indentation = lineContent[..(lineContent.Length - lineContent.AsSpan().TrimStart().Length)];
            if (indentation.StartsWith(document.Margin, StringComparison.Ordinal))
            {
                indentation = indentation[document.Margin.Length..];
            }
        }

        var output = new List<ReferenceRenderer.SourceLine>(lines.Count + 3);
        if (match.Format is ReferenceFormat.XmlDocCode)
        {
            output.Add(new("<code>", targetEol));
        }

        foreach (var line in lines)
        {
            output.Add(new(EscapeText(line.Text), ReferenceRenderer.GetLineEndOfLine(line, eolOption, targetEol)));
        }

        if (match.Format is ReferenceFormat.XmlDocCode)
        {
            output.Add(new("</code>", targetEol));
            if (match.SourceLink is not null)
            {
                var url = await ReferenceRenderer.GetSourceLinkAsync(filePath, match, linkRange, cancellationToken);
                output.Add(new($"<para><see href=\"{EscapeAttribute(url)}\">source code</see></para>", targetEol));
            }
        }

        var text = new StringBuilder();
        int start;
        int end;
        if (target.EndTagStart is null)
        {
            // <code ref:source="..." /> => <code ref:source="...">content</code>
            start = target.StartTagEnd - 2; // "/>"
            while (start > target.StartTagStart && document.Xml[start - 1] is ' ' or '\t')
            {
                start--;
            }

            end = target.StartTagEnd;
            text.Append('>');
        }
        else
        {
            start = target.StartTagEnd;
            end = target.EndTagStart.Value;
        }

        text.Append(targetEol);
        foreach (var line in output)
        {
            text.Append(startLine.Prefix);
            if (line.Text.Length > 0)
            {
                text.Append(document.Margin).Append(indentation).Append(line.Text);
            }

            text.Append(line.Eol);
        }

        if (target.EndTagStart is null)
        {
            text.Append(startLine.Prefix).Append(document.Margin).Append(indentation).Append("</").Append(target.Name).Append('>');
        }
        else
        {
            var endLineIndex = document.GetLineIndex(end);
            var endLineStart = document.GetOffset(endLineIndex, 0);
            if (endLineStart > start && string.IsNullOrWhiteSpace(document.Xml[endLineStart..end]))
            {
                // The end tag is on its own line: keep this line as-is
                text.Append(document.Lines[endLineIndex].Prefix);
                end = endLineStart;
            }
            else
            {
                text.Append(startLine.Prefix).Append(document.Margin).Append(indentation);
            }
        }

        return new Replacement(start, end, text.ToString());
    }

    private static string EscapeText(string value)
    {
        if (value.AsSpan().IndexOfAny('&', '<', '>') < 0)
            return value;

        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string EscapeAttribute(string value) => EscapeText(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    internal sealed record UpdateResult(string Content, int ReferenceCount, bool HasErrors);

    /// <summary>A line of a documentation comment. <paramref name="Prefix"/> contains the indentation and "///".</summary>
    private sealed record DocLine(string Prefix, string Content, string Eol);

    /// <summary>A replacement of the range [<paramref name="Start"/>, <paramref name="End"/>[ of <see cref="DocComment.Xml"/>. <paramref name="Text"/> is the physical text, including the "///" prefixes.</summary>
    private sealed record Replacement(int Start, int End, string Text);

    private sealed record Target(ReferenceMatch Match, string Name, int StartTagStart, int StartTagEnd, int? EndTagStart, int Line);

    private sealed record PendingTarget(ReferenceMatch Match, string Name, int StartTagStart, int StartTagEnd, int Line)
    {
        public Target ToTarget(int? endTagStart) => new(Match, Name, StartTagStart, StartTagEnd, endTagStart, Line);
    }

    /// <summary>A block of consecutive documentation comment lines.</summary>
    private sealed class DocComment
    {
        private readonly int[] _lineOffsets;

        public DocComment(List<DocLine> lines)
        {
            Lines = [.. lines];
            _lineOffsets = new int[lines.Count];

            var xml = new StringBuilder();
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    xml.Append('\n');
                }

                _lineOffsets[i] = xml.Length;
                xml.Append(lines[i].Content);
            }

            Xml = xml.ToString();
            Margin = GetMargin(lines);
        }

        public IReadOnlyList<DocLine> Lines { get; }

        /// <summary>The XML content of the comment, without the "///" prefixes. Lines are separated by '\n'.</summary>
        public string Xml { get; }

        /// <summary>
        /// The whitespace that separates "///" from the content on every line (usually a single space).
        /// As for the C# compiler, it is not part of the indentation of the content.
        /// </summary>
        public string Margin { get; }

        public int GetOffset(int lineIndex, int column) => _lineOffsets[lineIndex] + column;

        public int GetLineIndex(int offset)
        {
            var index = Array.BinarySearch(_lineOffsets, offset);
            return index >= 0 ? index : ~index - 1;
        }

        /// <summary>Appends the physical text of the comment after applying the replacements.</summary>
        public void AppendTo(StringBuilder result, List<Replacement> replacements)
        {
            result.Append(Lines[0].Prefix);
            var position = 0;
            foreach (var replacement in replacements.OrderBy(r => r.Start))
            {
                AppendXml(result, position, replacement.Start);
                result.Append(replacement.Text);
                position = replacement.End;
            }

            AppendXml(result, position, Xml.Length);
            result.Append(Lines[^1].Eol);
        }

        private void AppendXml(StringBuilder result, int start, int end)
        {
            var segmentStart = start;
            for (var i = start; i < end; i++)
            {
                if (Xml[i] == '\n')
                {
                    // Restore the original end of line and prefix of the next line
                    var lineIndex = GetLineIndex(i);
                    result.Append(Xml, segmentStart, i - segmentStart);
                    result.Append(Lines[lineIndex].Eol).Append(Lines[lineIndex + 1].Prefix);
                    segmentStart = i + 1;
                }
            }

            result.Append(Xml, segmentStart, end - segmentStart);
        }

        private static string GetMargin(List<DocLine> lines)
        {
            string? margin = null;
            foreach (var line in lines)
            {
                if (line.Content.Length == 0)
                    continue;

                if (!char.IsWhiteSpace(line.Content[0]))
                    return "";

                if (margin is null)
                {
                    margin = line.Content[..1];
                }
                else if (line.Content[0] != margin[0])
                {
                    return "";
                }
            }

            return margin ?? "";
        }
    }
}
