using System.Diagnostics;
using Meziantou.Framework;

namespace Meziantou.FileReferencer.Tests;

public sealed class MarkdownFencedCodeBlockTests : IAsyncLifetime
{
    private const string SampleCode = """
        namespace Sample;

        public class Example
        {
            public void Method()
            {
                #region BasicUsage
                var value = 42;
                Console.WriteLine(value);
                #endregion
            }
        }
        """;

    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _directory.DisposeAsync();

    [Fact]
    public async Task WholeFile()
    {
        CreateFile("src/Example.cs", SampleCode);
        await AssertUpdated("docs/readme.md", """
            # Title
            <!-- ref:../src/Example.cs;format=md-fenced-code-block -->
            <!-- endref -->
            Footer
            """, """
            # Title
            <!-- ref:../src/Example.cs;format=md-fenced-code-block -->
            ```csharp
            namespace Sample;

            public class Example
            {
                public void Method()
                {
                    #region BasicUsage
                    var value = 42;
                    Console.WriteLine(value);
                    #endregion
                }
            }
            ```
            <!-- endref -->
            Footer
            """);
    }

    [Fact]
    public async Task ReplacesExistingContent()
    {
        CreateFile("sample.txt", "new content");
        await AssertUpdated("readme.md", """
            <!-- ref:sample.txt;format=md-fenced-code-block -->
            ```
            old content
            ```
            <!-- endref -->
            """, """
            <!-- ref:sample.txt;format=md-fenced-code-block -->
            ```
            new content
            ```
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task IsIdempotent()
    {
        CreateFile("src/Example.cs", SampleCode);
        var path = CreateFile("readme.md", """
            <!-- ref:src/Example.cs;region=BasicUsage;format=md-fenced-code-block;dedent=true;source-link=https://example.com/Example.cs -->
            <!-- endref -->
            """);

        Assert.Equal(0, Run(path));
        var first = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var lastWriteTime = File.GetLastWriteTimeUtc(path);
        Assert.Equal(0, Run(path));
        Assert.Equal(first, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(lastWriteTime, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task LineRange()
    {
        CreateFile("src/Example.cs", SampleCode);
        await AssertUpdated("readme.md", """
            <!-- ref:src/Example.cs;lines=3-4;format=md-fenced-code-block -->
            <!-- endref -->
            """, """
            <!-- ref:src/Example.cs;lines=3-4;format=md-fenced-code-block -->
            ```csharp
            public class Example
            {
            ```
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task SingleLine()
    {
        CreateFile("src/Example.cs", SampleCode);
        await AssertUpdated("readme.md", """
            <!-- ref:src/Example.cs;lines=1;format=md-fenced-code-block -->
            <!-- endref -->
            """, """
            <!-- ref:src/Example.cs;lines=1;format=md-fenced-code-block -->
            ```csharp
            namespace Sample;
            ```
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task LineRangeWithoutFormat()
    {
        CreateFile("sample.txt", "line1\nline2\nline3\n");
        await AssertUpdated("readme.md", """
            <!-- ref:sample.txt;lines=2-3 -->
            <!-- endref -->
            """, """
            <!-- ref:sample.txt;lines=2-3 -->
            line2
            line3
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task Region()
    {
        CreateFile("src/Example.cs", SampleCode);
        await AssertUpdated("readme.md", """
            <!-- ref:src/Example.cs;region=BasicUsage;format=md-fenced-code-block -->
            <!-- endref -->
            """, """
            <!-- ref:src/Example.cs;region=BasicUsage;format=md-fenced-code-block -->
            ```csharp
                    var value = 42;
                    Console.WriteLine(value);
            ```
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task RegionDedent()
    {
        CreateFile("src/Example.cs", SampleCode);
        await AssertUpdated("readme.md", """
            <!-- ref:src/Example.cs;region=BasicUsage;format=md-fenced-code-block;dedent=true -->
            <!-- endref -->
            """, """
            <!-- ref:src/Example.cs;region=BasicUsage;format=md-fenced-code-block;dedent=true -->
            ```csharp
            var value = 42;
            Console.WriteLine(value);
            ```
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task DedentPreservesRelativeIndentationAndBlankLines()
    {
        CreateFile("sample.cs", "    if (true)\n    {\n\n        Run();\n  \n    }\n");
        await AssertUpdated("readme.md", """
            <!-- ref:sample.cs;format=md-fenced-code-block;dedent=true -->
            <!-- endref -->
            """, """
            <!-- ref:sample.cs;format=md-fenced-code-block;dedent=true -->
            ```csharp
            if (true)
            {

                Run();

            }
            ```
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task NestedRegions()
    {
        CreateFile("sample.cs", """
            #region Outer
            var a = 1;
            #region Inner
            var b = 2;
            #endregion
            var c = 3;
            #endregion
            #region Other
            var d = 4;
            #endregion
            """);
        await AssertUpdated("readme.md", """
            <!-- ref:sample.cs;region=Outer;format=md-fenced-code-block -->
            <!-- endref -->

            <!-- ref:sample.cs;region=Inner;format=md-fenced-code-block -->
            <!-- endref -->
            """, """
            <!-- ref:sample.cs;region=Outer;format=md-fenced-code-block -->
            ```csharp
            var a = 1;
            #region Inner
            var b = 2;
            #endregion
            var c = 3;
            ```
            <!-- endref -->

            <!-- ref:sample.cs;region=Inner;format=md-fenced-code-block -->
            ```csharp
            var b = 2;
            ```
            <!-- endref -->
            """);
    }

    [Theory]
    [InlineData("sample.cs", "", "csharp")]
    [InlineData("sample.json", "", "json")]
    [InlineData("sample.YML", "", "yaml")]
    [InlineData("Dockerfile", "", "dockerfile")]
    [InlineData("sample.unknown", "", "")]
    [InlineData("sample.cs", ";language=cs", "cs")]
    [InlineData("sample.unknown", ";language=text", "text")]
    [InlineData("sample.cs", ";language=none", "")]
    public async Task Language(string fileName, string options, string expectedLanguage)
    {
        CreateFile(fileName, "content");
        await AssertUpdated("readme.md", $"""
            <!-- ref:{fileName};format=md-fenced-code-block{options} -->
            <!-- endref -->
            """, $"""
            <!-- ref:{fileName};format=md-fenced-code-block{options} -->
            ```{expectedLanguage}
            content
            ```
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task EmbeddedBackticks()
    {
        CreateFile("sample.md", """
            Use `code` here:
            ````csharp
            var a = 1;
            ````
            """);
        await AssertUpdated("readme.md", """
            <!-- ref:sample.md;format=md-fenced-code-block -->
            <!-- endref -->
            """, """
            <!-- ref:sample.md;format=md-fenced-code-block -->
            `````markdown
            Use `code` here:
            ````csharp
            var a = 1;
            ````
            `````
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task Indentation()
    {
        CreateFile("sample.cs", "if (true)\n{\n\n    Run();\n}");
        await AssertUpdated("readme.md", """
            - Item
              <!-- ref:sample.cs;format=md-fenced-code-block;source-link=https://example.com/sample.cs -->
              <!-- endref -->
            """, """
            - Item
              <!-- ref:sample.cs;format=md-fenced-code-block;source-link=https://example.com/sample.cs -->
              ```csharp
              if (true)
              {

                  Run();
              }
              ```

              [source code](https://example.com/sample.cs)
              <!-- endref -->
            """);
    }

    [Fact]
    public async Task IndentationDisabled()
    {
        CreateFile("sample.cs", "Run();");
        await AssertUpdated("readme.md", """
              <!-- ref:sample.cs;format=md-fenced-code-block;indent=false -->
              <!-- endref -->
            """, """
              <!-- ref:sample.cs;format=md-fenced-code-block;indent=false -->
            ```csharp
            Run();
            ```
              <!-- endref -->
            """);
    }

    [Theory]
    [InlineData("\r\n", "", "\r\n")]
    [InlineData("\n", "", "\n")]
    [InlineData("\r\n", ";eol=lf", "\n")]
    [InlineData("\n", ";eol=crlf", "\r\n")]
    public async Task EndOfLine(string documentEol, string options, string expectedEol)
    {
        File.WriteAllText(_directory.GetFullPath("sample.cs"), "  a\r\n  b\n  c\r\n");
        var path = _directory.GetFullPath("readme.md");
        File.WriteAllText(path, string.Join(documentEol, [
            "  <!-- ref:sample.cs;format=md-fenced-code-block;source-link=https://example.com/" + options + " -->",
            "  <!-- endref -->",
            ""]));

        Assert.Equal(0, Run(path));

        var markerEol = documentEol;
        var expected =
            "  <!-- ref:sample.cs;format=md-fenced-code-block;source-link=https://example.com/" + options + " -->" + markerEol +
            "  ```csharp" + expectedEol +
            "    a" + expectedEol +
            "    b" + expectedEol +
            "    c" + expectedEol +
            "  ```" + expectedEol +
            expectedEol +
            "  [source code](https://example.com/)" + expectedEol +
            "  <!-- endref -->" + markerEol;
        Assert.Equal(expected, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SourceLink_Explicit()
    {
        CreateFile("sample.cs", "Run();");
        await AssertUpdated("readme.md", """
            <!-- ref:sample.cs;format=md-fenced-code-block;lines=1;source-link=https://example.com/a/b?c=d#L1 -->
            <!-- endref -->
            """, """
            <!-- ref:sample.cs;format=md-fenced-code-block;lines=1;source-link=https://example.com/a/b?c=d#L1 -->
            ```csharp
            Run();
            ```

            [source code](https://example.com/a/b?c=d#L1)
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task SourceLink_Auto_LocalPathWithSpaces()
    {
        InitGitRepository("https://github.com/owner/repo.git", "main");
        CreateFile("tests/My Tests.cs", SampleCode);
        await AssertUpdated("docs/readme.md", """
            <!-- ref:../tests/My Tests.cs;lines=1;format=md-fenced-code-block;source-link=auto -->
            <!-- endref -->
            """, """
            <!-- ref:../tests/My Tests.cs;lines=1;format=md-fenced-code-block;source-link=auto -->
            ```csharp
            namespace Sample;
            ```

            [source code](https://github.com/owner/repo/blob/main/tests/My%20Tests.cs#L1)
            <!-- endref -->
            """);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("ssh://git@github.com/owner/repo.git")]
    public async Task SourceLink_Auto_LocalRegion(string remoteUrl)
    {
        InitGitRepository(remoteUrl, "release/v1");
        CreateFile("src/Example.cs", SampleCode);
        await AssertUpdated("docs/readme.md", """
            <!-- ref:../src/Example.cs;region=BasicUsage;format=md-fenced-code-block;dedent=true;source-link=auto -->
            <!-- endref -->
            """, """
            <!-- ref:../src/Example.cs;region=BasicUsage;format=md-fenced-code-block;dedent=true;source-link=auto -->
            ```csharp
            var value = 42;
            Console.WriteLine(value);
            ```

            [source code](https://github.com/owner/repo/blob/release/v1/src/Example.cs#L8-L9)
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task SourceLink_Auto_LocalFileAtRepositoryRoot()
    {
        InitGitRepository("https://github.com/owner/repo.git", "main");
        CreateFile("Example.cs", "Run();");
        await AssertUpdated("readme.md", """
            <!-- ref:Example.cs;format=md-fenced-code-block;source-link=auto -->
            <!-- endref -->
            """, """
            <!-- ref:Example.cs;format=md-fenced-code-block;source-link=auto -->
            ```csharp
            Run();
            ```

            [source code](https://github.com/owner/repo/blob/main/Example.cs)
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task SourceLink_Auto_LocalWithoutGitHubRemote()
    {
        InitGitRepository("https://gitlab.com/owner/repo.git", "main");
        CreateFile("Example.cs", "Run();");
        await AssertNotUpdated("readme.md", """
            <!-- ref:Example.cs;format=md-fenced-code-block;source-link=auto -->
            existing
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task SourceLink_Auto_LocalWithoutRemote()
    {
        InitGitRepository(remoteUrl: null, defaultBranch: null);
        CreateFile("Example.cs", "Run();");
        await AssertNotUpdated("readme.md", """
            <!-- ref:Example.cs;format=md-fenced-code-block;source-link=auto -->
            existing
            <!-- endref -->
            """);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/blob/v1.0.0/src/Example.cs", "https://github.com/owner/repo/blob/v1.0.0/src/Example.cs#L2-L3")]
    [InlineData("https://github.com/owner/repo/raw/v1.0.0/src/Example.cs", "https://github.com/owner/repo/blob/v1.0.0/src/Example.cs#L2-L3")]
    [InlineData("https://raw.githubusercontent.com/owner/repo/0123456789abcdef/src/Example.cs", "https://github.com/owner/repo/blob/0123456789abcdef/src/Example.cs#L2-L3")]
    public async Task SourceLink_Auto_Remote(string reference, string expectedUrl)
    {
        Assert.Equal(expectedUrl, await GetSourceLinkAsync(_directory.GetFullPath("readme.md"), reference, new LineRange(2, 3)));
    }

    [Fact]
    public async Task SourceLink_Auto_RemoteNotGitHub()
    {
        var exception = await Assert.ThrowsAsync<ReferenceException>(() => GetSourceLinkAsync(_directory.GetFullPath("readme.md"), "https://example.com/file.cs", lines: null));
        Assert.Contains("only GitHub URLs are supported", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lines=0")]
    [InlineData("lines=3-2")]
    [InlineData("lines=a-b")]
    [InlineData("lines=1-100")]
    [InlineData("lines=100")]
    [InlineData("region=Missing")]
    [InlineData("region=Duplicate")]
    [InlineData("region=Unclosed")]
    [InlineData("region=BasicUsage;lines=1")]
    [InlineData("format=unknown")]
    [InlineData("dedent=yes")]
    [InlineData("source-link=relative/path")]
    public async Task InvalidReference_DoesNotUpdate(string options)
    {
        CreateFile("sample.cs", """
            #region BasicUsage
            a
            #endregion
            #region Duplicate
            #endregion
            #region Duplicate
            #endregion
            #region Unclosed
            """);
        await AssertNotUpdated("readme.md", $"""
            <!-- ref:sample.cs;format=md-fenced-code-block;{options} -->
            existing content
            <!-- endref -->
            """);
    }

    [Fact]
    public async Task InvalidReference_OtherReferencesAreUpdated()
    {
        CreateFile("sample.cs", "Run();");
        var path = CreateFile("readme.md", """
            <!-- ref:sample.cs;lines=10 -->
            existing content
            <!-- endref -->
            <!-- ref:sample.cs -->
            <!-- endref -->
            """);

        Assert.Equal(1, Run(path));
        Assert.Equal(Normalize("""
            <!-- ref:sample.cs;lines=10 -->
            existing content
            <!-- endref -->
            <!-- ref:sample.cs -->
            Run();
            <!-- endref -->
            """), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingEndMarker_DoesNotUpdate()
    {
        CreateFile("sample.cs", "Run();");
        await AssertNotUpdated("readme.md", """
            <!-- ref:sample.cs -->
            existing content
            """);
    }

    private static Task<string> GetSourceLinkAsync(string filePath, string reference, LineRange? lines)
        => SourceLinkResolver.ResolveAsync(filePath, reference, lines, TestContext.Current.CancellationToken);

    private async Task AssertUpdated(string fileName, string content, string expected)
    {
        var path = CreateFile(fileName, content);
        Assert.Equal(0, Run(path));
        Assert.Equal(Normalize(expected), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private async Task AssertNotUpdated(string fileName, string content)
    {
        var path = CreateFile(fileName, content);
        Assert.Equal(1, Run(path));
        Assert.Equal(Normalize(content), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private string CreateFile(string fileName, string content)
    {
        var path = _directory.GetFullPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Normalize(content));
        return path;
    }

    private static string Normalize(string content) => content.ReplaceLineEndings("\n");

    private static int Run(params string[] arguments) => (int)typeof(Program).Assembly.EntryPoint!.Invoke(null, [arguments])!;

    private void InitGitRepository(string? remoteUrl, string? defaultBranch)
    {
        RunGit("init", "--quiet");
        if (remoteUrl is not null)
        {
            RunGit("remote", "add", "origin", remoteUrl);
        }

        if (defaultBranch is not null)
        {
            RunGit("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/" + defaultBranch);
        }
    }

    private void RunGit(params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _directory.FullPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
