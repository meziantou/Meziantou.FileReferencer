using System.ComponentModel;

namespace Meziantou.FileReferencer;
internal static class GitTrackedFiles
{
    /// <summary>Gets the files tracked by Git in the specified file or folder.</summary>
    /// <exception cref="GitException">The files cannot be listed (e.g. not a Git repository, or git is not available).</exception>
    public static async Task<IReadOnlyList<string>> GetFilesAsync(string path, bool recurse, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            // Paths are relative to the working directory and use '/' as separator
            var entries = await ListFilesAsync(path, [], cancellationToken);
            var result = new List<string>(entries.Length);
            foreach (var entry in entries)
            {
                if (!recurse && entry.Contains('/', StringComparison.Ordinal))
                    continue;

                // Skip files deleted from the working tree and submodules
                var filePath = Path.Combine(path, entry.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(filePath))
                {
                    result.Add(filePath);
                }
            }

            return result;
        }

        if (File.Exists(path))
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath) ?? throw new GitException("Cannot determine the containing directory.");
            var entries = await ListFilesAsync(directory, [":(literal)" + Path.GetFileName(fullPath)], cancellationToken);
            if (entries.Length > 0)
                return [path];

            Console.WriteLine($"Skipping file {path}: the file is not tracked by Git");
        }

        return [];
    }

    private static async Task<string[]> ListFilesAsync(string workingDirectory, string[] pathspecs, CancellationToken cancellationToken)
    {
        GitResult result;
        try
        {
            result = await Git.RunAsync(workingDirectory, ["ls-files", "-z", "--", .. pathspecs], cancellationToken);
        }
        catch (Win32Exception ex)
        {
            throw new GitException("git is not available.", ex);
        }

        if (result.ExitCode != 0)
        {
            var error = result.Error.Trim();
            throw new GitException(error.Length > 0 ? error : $"git exited with code {result.ExitCode}.");
        }

        return result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}

internal sealed class GitException : Exception
{
    public GitException()
    {
    }

    public GitException(string message)
        : base(message)
    {
    }

    public GitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
