using System.IO.Enumeration;
using Meziantou.Framework.Globbing;

namespace Meziantou.FileReferencer;

/// <summary>
/// Lists the files to process, skipping the ones matching the <c>--exclude</c> patterns.
/// The patterns use the .gitignore syntax and are matched against the path relative to the current directory.
/// </summary>
internal sealed class FileExclusions
{
    private readonly GlobCollection? _globs;
    private readonly string _baseDirectory;

    public FileExclusions(IReadOnlyCollection<string> patterns, string? baseDirectory = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
        if (patterns.Count > 0)
        {
            _globs = GlobCollection.ParseGitIgnore(string.Join('\n', patterns));
        }
    }

    public IEnumerable<string> GetFiles(string[] paths, bool recurse)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                if (IsExcluded(path, PathItemType.Directory))
                    continue;

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recurse,
                    AttributesToSkip = 0,
                    IgnoreInaccessible = false,
                };
                var files = new FileSystemEnumerable<string>(path, (ref entry) => entry.ToSpecifiedFullPath(), options)
                {
                    ShouldIncludePredicate = (ref entry) => !entry.IsDirectory && !IsExcluded(entry.ToFullPath(), PathItemType.File),
                    ShouldRecursePredicate = (ref entry) => !IsExcluded(entry.ToFullPath(), PathItemType.Directory),
                };

                foreach (var file in files)
                {
                    yield return file;
                }
            }
            else if (File.Exists(path))
            {
                if (!IsExcluded(path, PathItemType.File))
                {
                    yield return path;
                }
            }
        }
    }

    public bool IsExcluded(string path, PathItemType itemType)
    {
        if (_globs is null)
            return false;

        var relativePath = Path.GetRelativePath(_baseDirectory, Path.GetFullPath(path));
        if (relativePath == ".")
            return false;

        // The excluded ancestors of the path are taken into account, so excluding a folder excludes all its content
        return _globs.IsMatch(relativePath.Replace('\\', '/'), [], itemType);
    }
}
