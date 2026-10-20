using System.Diagnostics;
using Meziantou.Framework;

namespace Meziantou.FileReferencer.Tests;

public sealed class GitTrackedOnlyTests : IAsyncLifetime
{
    private const string Reference = """
        // ref:ref.txt
        // endref
        """;

    private const string UpdatedReference = """
        // ref:ref.txt
        ref
        // endref
        """;

    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _directory.DisposeAsync();

    [Fact]
    public async Task Directory_OnlyUpdatesTrackedFiles()
    {
        RunGit("init", "--quiet");
        CreateFile("ref.txt", "ref");
        var tracked = CreateFile("tracked.cs", Reference);
        var trackedInSubfolder = CreateFile("sub/tracked.cs", Reference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal));
        var staged = CreateFile("staged.cs", Reference);
        var untracked = CreateFile("untracked.cs", Reference);
        CreateFile(".gitignore", "ignored.cs");
        var ignored = CreateFile("ignored.cs", Reference);
        RunGit("add", "tracked.cs", "sub/tracked.cs", "staged.cs", ".gitignore");

        Assert.Equal(0, Run(_directory.FullPath, "--git-tracked-only"));

        Assert.Equal(Normalize(UpdatedReference), await ReadFileAsync(tracked));
        Assert.Equal(Normalize(UpdatedReference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal)), await ReadFileAsync(trackedInSubfolder));
        Assert.Equal(Normalize(UpdatedReference), await ReadFileAsync(staged));
        Assert.Equal(Normalize(Reference), await ReadFileAsync(untracked));
        Assert.Equal(Normalize(Reference), await ReadFileAsync(ignored));
    }

    [Fact]
    public async Task Directory_NoRecurse()
    {
        RunGit("init", "--quiet");
        CreateFile("ref.txt", "ref");
        var tracked = CreateFile("tracked.cs", Reference);
        var trackedInSubfolder = CreateFile("sub/tracked.cs", Reference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal));
        RunGit("add", ".");

        Assert.Equal(0, Run(_directory.FullPath, "--git-tracked-only", "--recurse", "false"));

        Assert.Equal(Normalize(UpdatedReference), await ReadFileAsync(tracked));
        Assert.Equal(Normalize(Reference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal)), await ReadFileAsync(trackedInSubfolder));
    }

    [Fact]
    public async Task Subdirectory_OnlyUpdatesTrackedFiles()
    {
        RunGit("init", "--quiet");
        CreateFile("ref.txt", "ref");
        var tracked = CreateFile("sub/tracked.cs", Reference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal));
        var untracked = CreateFile("sub/untracked.cs", Reference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal));
        RunGit("add", "sub/tracked.cs");

        Assert.Equal(0, Run(_directory.GetFullPath("sub"), "--git-tracked-only"));

        Assert.Equal(Normalize(UpdatedReference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal)), await ReadFileAsync(tracked));
        Assert.Equal(Normalize(Reference.Replace("ref.txt", "../ref.txt", StringComparison.Ordinal)), await ReadFileAsync(untracked));
    }

    [Fact]
    public async Task File_Tracked()
    {
        RunGit("init", "--quiet");
        CreateFile("ref.txt", "ref");
        var tracked = CreateFile("tracked [1].cs", Reference);
        RunGit("add", "tracked [1].cs");

        Assert.Equal(0, Run(tracked, "--git-tracked-only"));

        Assert.Equal(Normalize(UpdatedReference), await ReadFileAsync(tracked));
    }

    [Fact]
    public async Task File_Untracked()
    {
        RunGit("init", "--quiet");
        CreateFile("ref.txt", "ref");
        var untracked = CreateFile("untracked.cs", Reference);

        Assert.Equal(0, Run(untracked, "--git-tracked-only"));

        Assert.Equal(Normalize(Reference), await ReadFileAsync(untracked));
    }

    [Fact]
    public async Task FileReferences_Untracked()
    {
        RunGit("init", "--quiet");
        CreateFile("refs/LICENSE.txt", "dummy");
        CreateFile("FileReferences.json", """
            {
                "references": {
                    "LICENSE.txt": {
                        "ref": "./refs/LICENSE.txt"
                    }
                }
            }
            """);

        Assert.Equal(0, Run(_directory.FullPath, "--git-tracked-only"));
        Assert.False(File.Exists(_directory.GetFullPath("LICENSE.txt")));

        RunGit("add", "FileReferences.json");
        Assert.Equal(0, Run(_directory.FullPath, "--git-tracked-only"));
        Assert.Equal("dummy", await ReadFileAsync(_directory.GetFullPath("LICENSE.txt")));
    }

    [Fact]
    public async Task NotAGitRepository()
    {
        CreateFile("ref.txt", "ref");
        var path = CreateFile("test.cs", Reference);

        Assert.Equal(1, Run(_directory.FullPath, "--git-tracked-only"));

        Assert.Equal(Normalize(Reference), await ReadFileAsync(path));
    }

    private string CreateFile(string fileName, string content)
    {
        var path = _directory.GetFullPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Normalize(content));
        return path;
    }

    private static Task<string> ReadFileAsync(string path) => File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

    private static string Normalize(string content) => content.ReplaceLineEndings("\n");

    private static int Run(params string[] arguments) => (int)typeof(Program).Assembly.EntryPoint!.Invoke(null, [arguments])!;

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
