using Meziantou.Framework;

namespace Meziantou.FileReferencer.Tests;

public sealed class MissingFileTests : IAsyncLifetime
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _directory.DisposeAsync();

    [Theory]
    [InlineData("missing.txt")]
    [InlineData("missing-folder/missing.txt")]
    public async Task CommentMarker_ReportsErrorAndProcessesOtherFiles(string reference)
    {
        CreateFile("ref.txt", "ref");
        var invalidContent = $"""
            // ref:{reference}
            old
            // endref
            """;
        var invalidPath = CreateFile("invalid.cs", invalidContent);
        var validPath = CreateFile("valid.cs", """
            // ref:ref.txt
            // endref
            """);

        Assert.Equal(1, Run(_directory.FullPath));
        Assert.Equal(Normalize(invalidContent), await File.ReadAllTextAsync(invalidPath, TestContext.Current.CancellationToken));
        Assert.Equal(Normalize("""
            // ref:ref.txt
            ref
            // endref
            """), await File.ReadAllTextAsync(validPath, TestContext.Current.CancellationToken));

        Assert.Equal(1, Run(_directory.FullPath, "--check"));
    }

    [Fact]
    public async Task CommentMarker_OtherReferencesOfTheFileAreUpdated()
    {
        CreateFile("ref.txt", "ref");
        var path = CreateFile("test.cs", """
            // ref:missing-folder/missing.txt
            old
            // endref
            // ref:ref.txt
            // endref
            """);

        Assert.Equal(1, Run(path));
        Assert.Equal(Normalize("""
            // ref:missing-folder/missing.txt
            old
            // endref
            // ref:ref.txt
            ref
            // endref
            """), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task XmlDoc_ReportsError()
    {
        var content = """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="missing-folder/missing.cs">old</code>
            class A;
            """;
        var path = CreateFile("test.cs", content);

        Assert.Equal(1, Run(path));
        Assert.Equal(Normalize(content), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FileReferences_ReportsErrorAndProcessesOtherReferences()
    {
        CreateFile("refs/LICENSE.txt", "dummy");
        CreateFile("README.txt", "old");
        CreateFile("FileReferences.json", """
            {
                "references": {
                    "README.txt": {
                        "ref": "./missing-folder/README.txt"
                    },
                    "LICENSE.txt": {
                        "ref": "./refs/LICENSE.txt"
                    }
                }
            }
            """);

        Assert.Equal(1, Run(_directory.FullPath));
        Assert.Equal("old", await File.ReadAllTextAsync(_directory.GetFullPath("README.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("dummy", await File.ReadAllTextAsync(_directory.GetFullPath("LICENSE.txt"), TestContext.Current.CancellationToken));
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
}
