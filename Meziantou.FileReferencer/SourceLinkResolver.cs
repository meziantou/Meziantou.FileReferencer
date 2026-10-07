using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.FileReferencer;
internal static partial class SourceLinkResolver
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<LocalGitLocation>>> _locationTasks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<Task<GitHubRepository>>> _repositoryTasks = new(StringComparer.Ordinal);

    // https://github.com/owner/repo(.git), http://user@github.com/owner/repo, ssh://git@github.com/owner/repo.git, git@github.com:owner/repo.git
    [GeneratedRegex(@"^(?:(?:https?|ssh|git)://(?:[^@/]+@)?github\.com(?::\d+)?/|[^@/]+@github\.com:)(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex GitHubRemoteUrlRegex { get; }

    /// <summary>Gets the browser-facing URL of the referenced file, optionally pointing to the specified lines.</summary>
    public static async Task<string> ResolveAsync(string filePath, string reference, LineRange? lines, CancellationToken cancellationToken)
    {
        string url;
        if (FileDownloader.TryGetRemoteUri(reference, out var uri))
        {
            url = GetRemoteGitHubUrl(uri) ?? throw new ReferenceException($"Cannot generate a source link for '{reference}': only GitHub URLs are supported. Use 'source-link=<url>' to set the link explicitly.");
        }
        else
        {
            var localPath = FileDownloader.GetLocalPath(filePath, reference);
            var directory = Path.GetDirectoryName(localPath) ?? throw new ReferenceException($"Cannot generate a source link for '{reference}': cannot determine the containing directory.");
            var location = await _locationTasks.GetOrAdd(directory, dir => new(() => GetLocalGitLocationAsync(dir, cancellationToken))).Value;
            var repository = await _repositoryTasks.GetOrAdd(location.RepositoryRoot, _ => new(() => GetGitHubRepositoryAsync(location.RepositoryRoot, cancellationToken))).Value;

            var relativePath = location.Prefix + Path.GetFileName(localPath);
            url = $"https://github.com/{repository.Owner}/{repository.Name}/blob/{EscapePath(repository.DefaultBranch)}/{EscapePath(relativePath)}";
        }

        if (lines is { } range)
        {
            url += range.Start == range.End
                ? string.Create(CultureInfo.InvariantCulture, $"#L{range.Start}")
                : string.Create(CultureInfo.InvariantCulture, $"#L{range.Start}-L{range.End}");
        }

        return url;
    }

    private static string? GetRemoteGitHubUrl(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // https://github.com/{owner}/{repo}/(blob|raw)/{revision}/{path}
        if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 5 && segments[2] is "blob" or "raw")
            return $"https://github.com/{segments[0]}/{segments[1]}/blob/{string.Join('/', segments[3..])}";

        // https://raw.githubusercontent.com/{owner}/{repo}/{revision}/{path}
        if (string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 4)
            return $"https://github.com/{segments[0]}/{segments[1]}/blob/{string.Join('/', segments[2..])}";

        return null;
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static async Task<LocalGitLocation> GetLocalGitLocationAsync(string directory, CancellationToken cancellationToken)
    {
        var root = await RunGitAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (root is null)
            throw new ReferenceException($"Cannot generate a source link: '{directory}' is not in a Git repository.");

        // Use the prefix computed by git rather than a relative path to be resilient to symbolic links
        var prefix = await RunGitAsync(directory, ["rev-parse", "--show-prefix"], cancellationToken) ?? "";
        return new LocalGitLocation(root, prefix);
    }

    private static async Task<GitHubRepository> GetGitHubRepositoryAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var remoteUrl = await RunGitAsync(repositoryRoot, ["remote", "get-url", "origin"], cancellationToken);
        if (remoteUrl is null)
            throw new ReferenceException($"Cannot generate a source link: the Git repository '{repositoryRoot}' has no 'origin' remote.");

        var match = GitHubRemoteUrlRegex.Match(remoteUrl);
        if (!match.Success)
            throw new ReferenceException($"Cannot generate a source link: the 'origin' remote of '{repositoryRoot}' ({remoteUrl}) is not a GitHub repository. Use 'source-link=<url>' to set the link explicitly.");

        var defaultBranch = await GetDefaultBranchAsync(repositoryRoot, cancellationToken);
        if (defaultBranch is null)
            throw new ReferenceException($"Cannot generate a source link: cannot determine the default branch of the 'origin' remote of '{repositoryRoot}'. You can run 'git remote set-head origin --auto' to configure it.");

        return new GitHubRepository(match.Groups["owner"].Value, match.Groups["repo"].Value, defaultBranch);
    }

    private static async Task<string?> GetDefaultBranchAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        const string LocalPrefix = "refs/remotes/origin/";
        var symbolicRef = await RunGitAsync(repositoryRoot, ["symbolic-ref", "--quiet", "refs/remotes/origin/HEAD"], cancellationToken);
        if (symbolicRef is not null && symbolicRef.StartsWith(LocalPrefix, StringComparison.Ordinal))
            return symbolicRef[LocalPrefix.Length..];

        // origin/HEAD is not always set (e.g. shallow clones in CI), so ask the remote
        const string RemotePrefix = "ref: refs/heads/";
        var remoteHead = await RunGitAsync(repositoryRoot, ["ls-remote", "--symref", "origin", "HEAD"], cancellationToken);
        if (remoteHead is not null)
        {
            foreach (var (line, _) in remoteHead.SplitLines())
            {
                if (line.StartsWith(RemotePrefix, StringComparison.Ordinal))
                {
                    var tabIndex = line.IndexOf('\t', StringComparison.Ordinal);
                    if (tabIndex > RemotePrefix.Length)
                        return line[RemotePrefix.Length..tabIndex];
                }
            }
        }

        return null;
    }

    private static async Task<string?> RunGitAsync(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            await errorTask;
            if (process.ExitCode != 0)
                return null;

            output = output.Trim();
            return output.Length == 0 ? null : output;
        }
        catch (Win32Exception ex)
        {
            throw new ReferenceException("Cannot generate a source link: git is not available.", ex);
        }
    }

    private sealed record LocalGitLocation(string RepositoryRoot, string Prefix);
    private sealed record GitHubRepository(string Owner, string Name, string DefaultBranch);
}
