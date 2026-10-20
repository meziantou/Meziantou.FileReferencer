using Meziantou.Framework;

namespace Meziantou.FileReferencer.Tests;

public sealed class CheckTests : IAsyncLifetime
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _directory.DisposeAsync();

    [Fact]
    public async Task CommentMarker_Outdated()
    {
        CreateFile("ref.txt", "ref");
        var content = """
            // ref:ref.txt
            old
            // endref
            """;
        var path = CreateFile("test.cs", content);

        Assert.Equal(1, Run(path, "--check"));
        Assert.Equal(Normalize(content), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CommentMarker_UpToDate()
    {
        CreateFile("ref.txt", "ref");
        var path = CreateFile("test.cs", """
            // ref:ref.txt
            // endref
            """);

        Assert.Equal(1, Run(path, "--check"));
        Assert.Equal(0, Run(path));
        Assert.Equal(0, Run(path, "--check"));
    }

    [Fact]
    public async Task XmlDoc_Outdated()
    {
        CreateFile("sample.txt", "sample");
        var content = """
            /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="sample.txt"></code>
            class A;
            """;
        var path = CreateFile("test.cs", content);

        Assert.Equal(1, Run(path, "--check"));
        Assert.Equal(Normalize(content), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal(0, Run(path));
        Assert.Equal(0, Run(path, "--check"));
    }

    [Fact]
    public void Directory_ReportsOutdatedFiles()
    {
        CreateFile("ref.txt", "ref");
        CreateFile("up-to-date.cs", """
            // ref:ref.txt
            ref
            // endref
            """);
        CreateFile("sub/outdated.cs", """
            // ref:../ref.txt
            // endref
            """);

        Assert.Equal(1, Run(_directory.FullPath, "--check"));
        Assert.Equal(0, Run(_directory.FullPath, "--check", "--recurse", "false"));
    }

    [Fact]
    public void FileReferences_MissingFile()
    {
        CreateFile("refs/LICENSE.txt", "dummy");
        CreateFileReferences();

        Assert.Equal(1, Run(_directory.FullPath, "--check"));
        Assert.False(File.Exists(_directory.GetFullPath("LICENSE.txt")));
    }

    [Fact]
    public async Task FileReferences_Outdated()
    {
        CreateFile("refs/LICENSE.txt", "dummy");
        CreateFile("LICENSE.txt", "old");
        CreateFileReferences();

        Assert.Equal(1, Run(_directory.FullPath, "--check"));
        Assert.Equal("old", await File.ReadAllTextAsync(_directory.GetFullPath("LICENSE.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void FileReferences_UpToDate()
    {
        CreateFile("refs/LICENSE.txt", "dummy");
        CreateFile("LICENSE.txt", "dummy");
        CreateFileReferences();

        Assert.Equal(0, Run(_directory.FullPath, "--check"));
    }

    private void CreateFileReferences()
    {
        CreateFile("FileReferences.json", """
            {
                "references": {
                    "LICENSE.txt": {
                        "ref": "./refs/LICENSE.txt"
                    }
                }
            }
            """);
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
