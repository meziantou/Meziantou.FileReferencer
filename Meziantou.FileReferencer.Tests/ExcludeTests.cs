using Meziantou.Framework;
using Meziantou.Framework.Globbing;

namespace Meziantou.FileReferencer.Tests;

// The paths of the temporary directory are outside of the current directory, so these tests use patterns that are not anchored
public sealed class ExcludeTests : IAsyncLifetime
{
    // Looks like a reference to a missing file, as the comments of the public suffix list
    private const string InvalidReference = """
        // ref:missing-folder/missing.txt
        // endref
        """;

    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _directory.DisposeAsync();

    [Fact]
    public void FileName()
    {
        CreateFile("data/list.dat", InvalidReference);

        Assert.Equal(1, Run(_directory.FullPath));
        Assert.Equal(0, Run(_directory.FullPath, "--exclude", "list.dat"));
        Assert.Equal(0, Run(_directory.FullPath, "--exclude", "*.dat"));
        Assert.Equal(0, Run(_directory.FullPath, "--exclude", "**/data/list.dat"));
        Assert.Equal(1, Run(_directory.FullPath, "--exclude", "other.dat"));
    }

    [Fact]
    public void ExplicitFiles()
    {
        var invalidPath = CreateFile("data/list.dat", InvalidReference);
        var validPath = CreateFile("valid.txt", "");

        Assert.Equal(1, Run(validPath, invalidPath));
        Assert.Equal(0, Run(validPath, invalidPath, "--exclude", "list.dat"));
        Assert.Equal(0, Run(validPath, invalidPath, "--exclude", "data/"));
    }

    [Fact]
    public void Folder()
    {
        CreateFile("data/sub/list.dat", InvalidReference);

        Assert.Equal(0, Run(_directory.FullPath, "--exclude", "data/"));
        Assert.Equal(0, Run(_directory.FullPath, "--exclude", "sub"));
        Assert.Equal(1, Run(_directory.FullPath, "--exclude", "sub/other/"));
        Assert.Equal(0, Run(_directory.GetFullPath("data/sub"), "--exclude", "sub/"));
    }

    [Fact]
    public async Task OtherFilesAreProcessed()
    {
        CreateFile("ref.txt", "ref");
        CreateFile("list.dat", InvalidReference);
        var path = CreateFile("test.cs", """
            // ref:ref.txt
            // endref
            """);

        Assert.Equal(0, Run(_directory.FullPath, "--exclude", "*.dat"));
        Assert.Equal(Normalize("""
            // ref:ref.txt
            ref
            // endref
            """), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MultiplePatterns()
    {
        CreateFile("a.dat", InvalidReference);
        CreateFile("b.txt", InvalidReference);

        Assert.Equal(1, Run(_directory.FullPath, "--exclude", "*.dat"));
        Assert.Equal(0, Run(_directory.FullPath, "--exclude", "*.dat", "--exclude", "*.txt"));
        Assert.Equal(1, Run(_directory.FullPath, "--exclude", "*.dat", "--exclude", "*.txt", "--exclude", "!b.txt"));
    }

    [Fact]
    public void PatternsAreRelativeToTheBaseDirectory()
    {
        var exclusions = new FileExclusions(["/data/list.dat"], _directory.FullPath);

        Assert.True(exclusions.IsExcluded(_directory.GetFullPath("data/list.dat"), PathItemType.File));
        Assert.False(exclusions.IsExcluded(_directory.GetFullPath("sub/data/list.dat"), PathItemType.File));
        Assert.Equal([_directory.GetFullPath("sub/data/list.dat")], exclusions.GetFiles(
            [CreateFile("data/list.dat", ""), CreateFile("sub/data/list.dat", "")], recurse: true));
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
