using System.Text.RegularExpressions;

namespace Meziantou.FileReferencer;
static partial class Regexes
{
    public const string IndentationGroupName = "indentation";
    public const string ReferenceGroupName = "reference";
    private const string ReferenceSectionStart = "ref(erence)?:(?<reference>.+?)";
    private const string ReferenceSectionEnd = "endref(erence)?(:(?<reference>.+))?";
    private const string IndentationRegex = "(?<indentation>\\s*)";
    private const string OptionsRegex = "(?<options>;(?<name>[^=;]+)=(?<value>[^;]*))*;?";

    [GeneratedRegex($"^{IndentationRegex};\\s*{ReferenceSectionStart}\\s*{OptionsRegex}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SemiColonRegexStart { get; }

    [GeneratedRegex($"^{IndentationRegex};\\s*{ReferenceSectionEnd}\\s*{OptionsRegex}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SemiColonRegexEnd { get; }

    [GeneratedRegex($"^{IndentationRegex}<!--\\s*{ReferenceSectionStart}\\s*{OptionsRegex}\\s*-->\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex HtmlCommentRegexStart { get; }

    [GeneratedRegex($"^\\s*<!--\\s*{ReferenceSectionEnd}\\s*{OptionsRegex}\\s*-->\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex HtmlCommentRegexEnd { get; }

    [GeneratedRegex($"^{IndentationRegex}--\\s*{ReferenceSectionStart}\\s*{OptionsRegex}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SqlCommentRegexStart { get; }

    [GeneratedRegex($"^\\s*--\\s*{ReferenceSectionEnd}\\s*{OptionsRegex}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SqlCommentRegexEnd { get; }

    [GeneratedRegex($"^{IndentationRegex}#\\s*{ReferenceSectionStart}\\s*{OptionsRegex}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SharpRegexStart { get; }

    [GeneratedRegex($"^\\s*#\\s*{ReferenceSectionEnd}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SharpRegexEnd { get; }

    [GeneratedRegex($"^{IndentationRegex}//\\s*{ReferenceSectionStart}\\s*{OptionsRegex}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex DoubleSlashRegexStart { get; }

    [GeneratedRegex($"^\\s*//\\s*{ReferenceSectionEnd}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex DoubleSlashRegexEnd { get; }

    [GeneratedRegex($"^{IndentationRegex}/\\*+\\s*{ReferenceSectionStart}\\s*{OptionsRegex}\\s*\\*/\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SlashStarRegexStart { get; }

    [GeneratedRegex($"^\\s*/\\*+\\s*{ReferenceSectionEnd}\\s*\\*/\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex SlashStarRegexEnd { get; }

    [GeneratedRegex($"^{IndentationRegex}#region\\s*{ReferenceSectionStart}\\s*{OptionsRegex}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex CSharpRegionRegexStart { get; }

    [GeneratedRegex($"^\\s*#endregion\\s*{ReferenceSectionEnd}\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex CSharpRegionRegexEndStrict { get; }

    [GeneratedRegex($"^\\s*#endregion(\\s*|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    public static partial Regex CSharpRegionRegexEnd { get; }
}
