using System.Diagnostics;
using Meziantou.Framework;

namespace Meziantou.FileReferencer.Tests;

public sealed class XmlDocReferenceTests : IAsyncLifetime
{
    private const string SampleCode = """
        namespace Sample;

        public class Example
        {
            public void Method()
            {
                #region BasicUsage
                var items = new List<int> { 1, 2 };

                if (items.Count > 0 && items[0] == 1)
                {
                    Console.WriteLine("<ok>");
                }
                #endregion
            }
        }
        """;

    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _directory.DisposeAsync();

    [Fact]
    public async Task CodeElement()
    {
        CreateFile("samples/Example.cs", SampleCode);
        await AssertUpdated("src/Class.cs", """
            namespace Sample;

            public class Class
            {
                /// <summary>Summary</summary>
                /// <example>
                /// Handwritten explanation stays outside the replacement target.
                /// <code xmlns:ref="urn:meziantou:file-referencer"
                ///       ref:source="../samples/Example.cs"
                ///       ref:region="BasicUsage"
                ///       ref:dedent="true">
                /// </code>
                /// </example>
                public void Method() { }
            }
            """, """
            namespace Sample;

            public class Class
            {
                /// <summary>Summary</summary>
                /// <example>
                /// Handwritten explanation stays outside the replacement target.
                /// <code xmlns:ref="urn:meziantou:file-referencer"
                ///       ref:source="../samples/Example.cs"
                ///       ref:region="BasicUsage"
                ///       ref:dedent="true">
                /// var items = new List&lt;int&gt; { 1, 2 };
                ///
                /// if (items.Count &gt; 0 &amp;&amp; items[0] == 1)
                /// {
                ///     Console.WriteLine("&lt;ok&gt;");
                /// }
                /// </code>
                /// </example>
                public void Method() { }
            }
            """);
    }

    [Fact]
    public async Task ReplacesExistingContent()
    {
        CreateFile("sample.txt", "new content");
        await AssertUpdated("Class.cs", """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// old content
            /// <b>old</b>
            /// </code>
            class Class { }
            """, """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// new content
            /// </code>
            class Class { }
            """);
    }

    [Theory]
    [InlineData("example")]
    [InlineData("remarks")]
    [InlineData("custom")]
    [InlineData("doc:custom")]
    public async Task AnyElementName(string elementName)
    {
        CreateFile("sample.txt", "a < b");
        await AssertUpdated("Class.cs", $"""
            /// <{elementName} xmlns:doc="urn:doc" xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"></{elementName}>
            class Class {"{ }"}
            """, $"""
            /// <{elementName} xmlns:doc="urn:doc" xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// a &lt; b
            /// </{elementName}>
            class Class {"{ }"}
            """);
    }

    [Fact]
    public async Task InheritedNamespaceDeclarationAndAlternativePrefix()
    {
        CreateFile("sample.txt", "content");
        await AssertUpdated("Class.cs", """
            /// <example xmlns:snippet="urn:meziantou:file-referencer">
            ///   <para>Text</para>
            ///   <code snippet:source="sample.txt">
            ///   </code>
            ///   <code snippet:source="sample.txt" snippet:indent="false">
            ///   </code>
            /// </example>
            class Class { }
            """, """
            /// <example xmlns:snippet="urn:meziantou:file-referencer">
            ///   <para>Text</para>
            ///   <code snippet:source="sample.txt">
            ///   content
            ///   </code>
            ///   <code snippet:source="sample.txt" snippet:indent="false">
            /// content
            ///   </code>
            /// </example>
            class Class { }
            """);
    }

    [Fact]
    public async Task AttributesWithoutTheNamespaceAreIgnored()
    {
        CreateFile("sample.txt", "content");

        // Unprefixed attributes are not in any namespace, even when a default namespace is declared
        await AssertUnchanged("Class.cs", """
            /// <code source="sample.txt" ref:source="sample.txt" xmlns:ref="urn:other"></code>
            /// <code xmlns="urn:meziantou:file-referencer" source="sample.txt"></code>
            class Class { }
            """, expectedExitCode: 0);
    }

    [Fact]
    public async Task SelfClosingElement()
    {
        CreateFile("sample.txt", "line1\nline2");
        await AssertUpdated("Class.cs", """
            /// <example>
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt" />
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"/> Text after
            /// </example>
            class Class { }
            """, """
            /// <example>
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// line1
            /// line2
            /// </code>
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// line1
            /// line2
            /// </code> Text after
            /// </example>
            class Class { }
            """);
    }

    [Fact]
    public async Task NestedXmlAndTextAroundTarget()
    {
        CreateFile("sample.txt", "content");
        await AssertUpdated("Class.cs", """
            /// <remarks>Before <c>code</c> <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"><b>old <i>nested</i></b></code> after <see cref="Class"/>.
            /// </remarks>
            class Class { }
            """, """
            /// <remarks>Before <c>code</c> <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// content
            /// </code> after <see cref="Class"/>.
            /// </remarks>
            class Class { }
            """);
    }

    [Fact]
    public async Task EmptySource()
    {
        CreateFile("sample.txt", "");
        await AssertUpdated("Class.cs", """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">old</code>
            class Class { }
            """, """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// </code>
            class Class { }
            """);
    }

    [Fact]
    public async Task WithoutMargin()
    {
        CreateFile("sample.txt", "content\n\n  indented");
        await AssertUpdated("Class.cs", """
                ///<code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
                ///</code>
                class Class { }
            """, """
                ///<code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
                ///content
                ///
                ///  indented
                ///</code>
                class Class { }
            """);
    }

    [Fact]
    public async Task LineRange()
    {
        CreateFile("samples/Example.cs", SampleCode);
        await AssertUpdated("Class.cs", """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="samples/Example.cs" ref:lines="3-4"></code>
            class Class { }
            """, """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="samples/Example.cs" ref:lines="3-4">
            /// public class Example
            /// {
            /// </code>
            class Class { }
            """);
    }

    [Fact]
    public async Task XmlDocCodeFormat_SourceLink()
    {
        CreateFile("sample.cs", "Run();");
        await AssertUpdated("Class.cs", """
            /// <example xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.cs" ref:format="xmldoc-code" ref:source-link="https://example.com/a?b=c&amp;d=&quot;e&quot;#L1">
            /// </example>
            class Class { }
            """, """
            /// <example xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.cs" ref:format="xmldoc-code" ref:source-link="https://example.com/a?b=c&amp;d=&quot;e&quot;#L1">
            /// <code>
            /// Run();
            /// </code>
            /// <para><see href="https://example.com/a?b=c&amp;d=&quot;e&quot;#L1">source code</see></para>
            /// </example>
            class Class { }
            """);
    }

    [Fact]
    public async Task XmlDocCodeFormat_WithoutSourceLink()
    {
        CreateFile("sample.cs", "Run();");
        await AssertUpdated("Class.cs", """
            /// <example xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.cs" ref:format="xmldoc-code">
            /// </example>
            class Class { }
            """, """
            /// <example xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.cs" ref:format="xmldoc-code">
            /// <code>
            /// Run();
            /// </code>
            /// </example>
            class Class { }
            """);
    }

    [Fact]
    public async Task XmlDocCodeFormat_SourceLinkAuto()
    {
        InitGitRepository("https://github.com/owner/repo.git", "main");
        CreateFile("samples/Example.cs", SampleCode);
        await AssertUpdated("src/Class.cs", """
            namespace Sample;

            /// <summary>Summary</summary>
            /// <example xmlns:ref="urn:meziantou:file-referencer"
            ///          ref:source="../samples/Example.cs"
            ///          ref:region="BasicUsage"
            ///          ref:dedent="true"
            ///          ref:format="xmldoc-code"
            ///          ref:source-link="auto">
            /// </example>
            public class Class { }
            """, """
            namespace Sample;

            /// <summary>Summary</summary>
            /// <example xmlns:ref="urn:meziantou:file-referencer"
            ///          ref:source="../samples/Example.cs"
            ///          ref:region="BasicUsage"
            ///          ref:dedent="true"
            ///          ref:format="xmldoc-code"
            ///          ref:source-link="auto">
            /// <code>
            /// var items = new List&lt;int&gt; { 1, 2 };
            ///
            /// if (items.Count &gt; 0 &amp;&amp; items[0] == 1)
            /// {
            ///     Console.WriteLine("&lt;ok&gt;");
            /// }
            /// </code>
            /// <para><see href="https://github.com/owner/repo/blob/main/samples/Example.cs#L8-L13">source code</see></para>
            /// </example>
            public class Class { }
            """);
    }

    [Fact]
    public async Task IsIdempotent()
    {
        CreateFile("samples/Example.cs", SampleCode);
        var path = CreateFile("Class.cs", """
            /// <example xmlns:ref="urn:meziantou:file-referencer">
            /// <code ref:source="samples/Example.cs" ref:region="BasicUsage" ref:dedent="true" />
            ///   <code ref:source="samples/Example.cs" ref:lines="1"></code>
            /// <remarks ref:source="samples/Example.cs" ref:lines="3-4" ref:format="xmldoc-code" ref:source-link="https://example.com"/>
            /// </example>
            class Class { }
            """);

        Assert.Equal(0, Run(path));
        var first = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("///   namespace Sample;", first, StringComparison.Ordinal);
        var lastWriteTime = File.GetLastWriteTimeUtc(path);
        Assert.Equal(0, Run(path));
        Assert.Equal(first, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(lastWriteTime, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData("\r\n", "", "\r\n")]
    [InlineData("\n", "", "\n")]
    [InlineData("\r\n", " ref:eol=\"lf\"", "\n")]
    [InlineData("\n", " ref:eol=\"crlf\"", "\r\n")]
    public async Task EndOfLine(string documentEol, string options, string expectedEol)
    {
        File.WriteAllText(_directory.GetFullPath("sample.cs"), "a\r\nb\nc\r\n");
        var path = _directory.GetFullPath("Class.cs");
        var startTag = "    /// <code xmlns:ref=\"urn:meziantou:file-referencer\" ref:source=\"sample.cs\"" + options + ">";
        File.WriteAllText(path, string.Join(documentEol, [
            "    /// <summary>",
            "    /// </summary>",
            startTag,
            "    /// </code>",
            "    class Class { }",
            ""]));

        Assert.Equal(0, Run(path));

        var expected =
            "    /// <summary>" + documentEol +
            "    /// </summary>" + documentEol +
            startTag + expectedEol +
            "    /// a" + expectedEol +
            "    /// b" + expectedEol +
            "    /// c" + expectedEol +
            "    /// </code>" + documentEol +
            "    class Class { }" + documentEol;
        Assert.Equal(expected, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommentMarkersAreStillSupported()
    {
        CreateFile("sample.txt", "content");
        await AssertUpdated("Class.cs", """
            // ref:sample.txt
            // endref
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"></code>
            //// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"></code>
            class Class { }
            """, """
            // ref:sample.txt
            content
            // endref
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// content
            /// </code>
            //// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"></code>
            class Class { }
            """);
    }

    [Fact]
    public async Task CommentMarker_XmlDocCodeFormatIsNotSupported()
    {
        CreateFile("sample.txt", "content");
        await AssertUnchanged("Class.cs", """
            // ref:sample.txt;format=xmldoc-code
            existing content
            // endref
            class Class { }
            """);
    }

    [Fact]
    public async Task MalformedCommentsWithoutReferencesAreIgnored()
    {
        await AssertUnchanged("Class.cs", """
            /// <summary>Unclosed
            /// <param name="a">&nbsp;</param>
            class Class { }
            """, expectedExitCode: 0);
    }

    [Theory]
    [InlineData("""ref:source="missing.txt" """)]
    [InlineData("""ref:region="BasicUsage" """)]
    [InlineData("""ref:source="" """)]
    [InlineData("""ref:source="sample.cs" ref:unknown="value" """)]
    [InlineData("""ref:source="sample.cs" ref:language="csharp" """)]
    [InlineData("""ref:source="sample.cs" ref:format="unknown" """)]
    [InlineData("""ref:source="sample.cs" ref:format="md-fenced-code-block" """)]
    [InlineData("""ref:source="sample.cs" ref:source-link="https://example.com" """)]
    [InlineData("""ref:source="sample.cs" ref:format="xmldoc-code" ref:source-link="relative" """)]
    [InlineData("""ref:source="sample.cs" ref:lines="0" """)]
    [InlineData("""ref:source="sample.cs" ref:lines="1-100" """)]
    [InlineData("""ref:source="sample.cs" ref:region="Missing" """)]
    [InlineData("""ref:source="sample.cs" ref:region="Duplicate" """)]
    [InlineData("""ref:source="sample.cs" ref:region="Unclosed" """)]
    [InlineData("""ref:source="sample.cs" ref:region="BasicUsage" ref:lines="1" """)]
    [InlineData("""ref:source="sample.cs" ref:dedent="yes" """)]
    [InlineData("""ref:source="sample.cs" ref:indent="yes" """)]
    [InlineData("""ref:source="sample.cs" ref:trim-final-lines="yes" """)]
    [InlineData("""ref:source="sample.cs" ref:eol="unknown" """)]
    [InlineData("""ref:source="sample.cs" ref:eol="1" """)]
    public async Task InvalidReference_DoesNotUpdate(string attributes)
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
        await AssertUnchanged("Class.cs", $"""
            /// <code xmlns:ref="urn:meziantou:file-referencer" {attributes}>
            /// existing content
            /// </code>
            class Class {"{ }"}
            """);
    }

    [Fact]
    public async Task InvalidXml_DoesNotUpdate()
    {
        CreateFile("sample.txt", "content");

        // The "ref" prefix is not declared
        await AssertUnchanged("Class.cs", """
            /// <code ref:source="sample.txt">urn:meziantou:file-referencer</code>
            class Class { }
            """);
        await AssertUnchanged("Class.cs", """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// </example>
            class Class { }
            """);
    }

    [Fact]
    public async Task NestedReferences_DoesNotUpdate()
    {
        CreateFile("sample.txt", "content");
        await AssertUnchanged("Class.cs", """
            /// <example xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt">
            /// <code ref:source="sample.txt"></code>
            /// </example>
            class Class { }
            """);
    }

    [Fact]
    public async Task InvalidReference_OtherReferencesAreUpdated()
    {
        CreateFile("sample.txt", "content");
        var path = CreateFile("Class.cs", """
            /// <example xmlns:ref="urn:meziantou:file-referencer">
            /// <code ref:source="sample.txt" ref:lines="10">existing</code>
            /// <code ref:source="sample.txt"></code>
            /// </example>
            class Class { }
            """);

        Assert.Equal(1, Run(path));
        Assert.Equal(Normalize("""
            /// <example xmlns:ref="urn:meziantou:file-referencer">
            /// <code ref:source="sample.txt" ref:lines="10">existing</code>
            /// <code ref:source="sample.txt">
            /// content
            /// </code>
            /// </example>
            class Class { }
            """), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlyCSharpFilesAreSupported()
    {
        CreateFile("sample.txt", "content");
        await AssertUnchanged("Class.ts", """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"></code>
            """, expectedExitCode: 0);
    }

    private async Task AssertUpdated(string fileName, string content, string expected)
    {
        var path = CreateFile(fileName, content);
        Assert.Equal(0, Run(path));
        Assert.Equal(Normalize(expected), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private async Task AssertUnchanged(string fileName, string content, int expectedExitCode = 1)
    {
        var path = CreateFile(fileName, content);
        Assert.Equal(expectedExitCode, Run(path));
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

    private void InitGitRepository(string remoteUrl, string defaultBranch)
    {
        RunGit("init", "--quiet");
        RunGit("remote", "add", "origin", remoteUrl);
        RunGit("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/" + defaultBranch);
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
