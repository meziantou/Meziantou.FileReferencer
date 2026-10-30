using System.Diagnostics.CodeAnalysis;

namespace Meziantou.FileReferencer;

/// <remarks>
/// Note that is only support regexes as Renovate also support regexes. Adding other kind of parsers would require more work and may not be usable in the cloud version of Renovate.
/// </remarks>
internal abstract class Parser
{
    private static readonly Parser DoubleSlashParser = new RegexParser(Regexes.DoubleSlashRegexStart, Regexes.DoubleSlashRegexEnd);
    private static readonly Parser SlashStarParser = new RegexParser(Regexes.SlashStarRegexStart, Regexes.SlashStarRegexEnd);
    private static readonly Parser SqlParser = new RegexParser(Regexes.SqlCommentRegexStart, Regexes.SqlCommentRegexEnd);
    private static readonly Parser SemiColonParser = new RegexParser(Regexes.SemiColonRegexStart, Regexes.SemiColonRegexEnd);
    private static readonly Parser SharpParser = new RegexParser(Regexes.SharpRegexStart, Regexes.SharpRegexEnd);
    private static readonly Parser XmlParser = new RegexParser(Regexes.HtmlCommentRegexStart, Regexes.HtmlCommentRegexEnd);
    private static Parser CreateCStyleCommentParser() => new MultiParser(endParserMatchStartParser: false, [DoubleSlashParser, SlashStarParser]);
    private static Parser CreateGenericParser() => new MultiParser(endParserMatchStartParser: true,
    [
        CreateCStyleCommentParser(),
        SharpParser,
        XmlParser,
    ]);
    private static readonly Dictionary<string, Func<Parser>> ParserByExtensions = CreateParsers();

    private static Dictionary<string, Func<Parser>> CreateParsers()
    {        
        var parserByExtension = new Dictionary<string, Func<Parser>>(StringComparer.OrdinalIgnoreCase)
        {
            [".editorconfig"] = () => SharpParser,
            [".cs"] = () => new CSharpParser(),
            [".css"] = () => SlashStarParser,
            [".htm"] = () => XmlParser,
            [".html"] = () => XmlParser,
            [".ini"] = () => SemiColonParser,
            [".json"] = () => DoubleSlashParser,
            [".json5"] = () => DoubleSlashParser,
            [".js"] = CreateCStyleCommentParser,
            [".less"] = CreateCStyleCommentParser,
            [".md"] = () => XmlParser,
            [".scss"] = CreateCStyleCommentParser,
            [".sh"] = () => SharpParser,
            [".sql"] = () => SqlParser,
            [".ts"] = CreateCStyleCommentParser,
            [".xml"] = () => XmlParser,
            [".yaml"] = () => SharpParser,
            [".yml"] = () => SharpParser,
        };

        return parserByExtension;
    }

    public static bool TryGet(string path, [NotNullWhen(true)] out Parser? parser)
    {
        var extension = Path.GetExtension(path);
        if (ParserByExtensions.TryGetValue(extension, out var foundParser))
        {
            parser = foundParser();
            return true;
        }

        if (string.Equals(Path.GetFileName(path), "dockerfile", StringComparison.OrdinalIgnoreCase))
        {
            parser = SharpParser;
            return true;
        }

        parser = CreateGenericParser();
        return true;
    }

    public abstract ReferenceMatch? MatchStart(string line);
    public abstract bool MatchEnd(string line);
}
