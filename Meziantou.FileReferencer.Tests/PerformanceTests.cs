using Meziantou.Framework;

namespace Meziantou.FileReferencer.Tests;

public sealed class PerformanceTests
{
    [Theory]
    [InlineData("", new string[0], new string[0])]
    [InlineData("abc", new[] { "abc" }, new[] { "" })]
    [InlineData("\n", new[] { "" }, new[] { "\n" })]
    [InlineData("a\r\nb\rc\n\nlast", new[] { "a", "b", "c", "", "last" }, new[] { "\r\n", "\r", "\n", "\n", "" })]
    [InlineData("a\r\nb\r\n", new[] { "a", "b" }, new[] { "\r\n", "\r\n" })]
    public void SplitLines_PreservesLinesAndSeparators(string content, string[] expectedLines, string[] expectedSeparators)
    {
        var lines = new List<string>();
        var separators = new List<string>();
        foreach (var (line, separator) in content.SplitLines())
        {
            lines.Add(line);
            separators.Add(separator);
        }

        Assert.Equal(expectedLines, lines);
        Assert.Equal(expectedSeparators, separators);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    public void SplitLines_AllocationsAreLinear(int lineCount)
    {
        var content = string.Concat(Enumerable.Repeat("a line of text\r\n", lineCount));
        foreach (var _ in "warmup\n".SplitLines())
        {
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        var totalLength = 0;
        foreach (var (line, _) in content.SplitLines())
        {
            totalLength += line.Length;
            count++;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(lineCount, count);
        Assert.Equal(lineCount * "a line of text".Length, totalLength);
        Assert.True(allocated < content.Length * 10L, $"Allocated {allocated} bytes for {content.Length} characters");
    }

    [Theory]
    [InlineData("unknown.txt")]
    [InlineData("unknown")]
    public void Parsers_DoNotShareReferenceState(string path)
    {
        Assert.True(Parser.TryGet(path, out var first));
        Assert.True(Parser.TryGet(path, out var second));
        Assert.NotNull(first.MatchStart("// ref:first.txt"));
        Assert.NotNull(second.MatchStart("<!-- ref:second.txt -->"));
        Assert.True(first.MatchEnd("// endref"));
        Assert.True(second.MatchEnd("<!-- endref -->"));
    }

    [Fact]
    public async Task XmlDoc_WithoutNamespaceReturnsOriginalString()
    {
        var content = new string(' ', 10_000) + "\n/// <malformed\nclass C;";
        var result = await XmlDocReferenceUpdater.UpdateAsync("test.cs", content, EndOfLineOption.Auto, TestContext.Current.CancellationToken);

        Assert.Same(content, result.Content);
        Assert.Equal(0, result.ReferenceCount);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task FileWithoutReferences_IsNotWritten()
    {
        await using var directory = TemporaryDirectory.Create();
        var path = directory.CreateTextFile("test.cs", "/// <malformed\nclass C;");
        var timestamp = SetOldTimestamp(path);

        Assert.Equal(0, Run(path));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task FileReferences_UnchangedFileIsNotWritten()
    {
        await using var directory = TemporaryDirectory.Create();
        directory.CreateTextFile("source.txt", "unchanged");
        var path = directory.CreateTextFile("target.txt", "unchanged");
        var manifest = directory.CreateTextFile("FileReferences.json", """
            {"references":{"target.txt":{"ref":"source.txt"}}}
            """);
        var timestamp = SetOldTimestamp(path);

        Assert.Equal(0, Run(manifest));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData("test.cs", "// REF:source.txt", "// ENDREF")]
    [InlineData("test.cs", "// REFERENCE:source.txt", "// ENDREFERENCE")]
    [InlineData("test.cs", "/* ref:source.txt */", "/* endref */")]
    [InlineData("test.cs", "#region ref:source.txt", "#endregion")]
    [InlineData(".editorconfig", "# ref:source.txt", "# endref")]
    [InlineData("test.ini", "; ref:source.txt", "; endref")]
    [InlineData("test.sql", "-- ref:source.txt", "-- endref")]
    public async Task ParserSelectionAndFastPath_PreserveReferences(string name, string start, string end)
    {
        await using var directory = TemporaryDirectory.Create();
        directory.CreateTextFile("source.txt", "included");
        var path = directory.CreateTextFile(name, $"{start}\n{end}\n");

        Assert.Equal(0, Run(path));
        Assert.Equal($"{start}\nincluded\n{end}\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private static DateTime SetOldTimestamp(string path)
    {
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return File.GetLastWriteTimeUtc(path);
    }

    private static int Run(params string[] arguments) => (int)typeof(Program).Assembly.EntryPoint!.Invoke(null, [arguments])!;
}
