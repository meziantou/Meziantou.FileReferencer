using System.CommandLine;
using System.Text;
using System.Text.Json;
using Meziantou.FileReferencer;
using Meziantou.Framework.Globbing;

var pathsArgument = new Argument<string[]>("path")
{
    Description = "The file or folder to update",
    Arity = ArgumentArity.OneOrMore,
};
var recurseOption = new Option<bool>("--recurse")
{
    Description = "Recurse into subfolders",
    DefaultValueFactory = _ => true,
};
var endOfLineOption = new Option<EndOfLineOption>("--end-of-line")
{
    Description = "Update end of line characters in the remote file. Allowed value: as-is, auto, cr, lf, crlf",
    DefaultValueFactory = _ => EndOfLineOption.Auto,
};
var checkOption = new Option<bool>("--check")
{
    Description = "Check that all references are up to date without modifying files. Exit with a non-zero exit code if a file is not up to date",
};
var excludeOption = new Option<string[]>("--exclude")
{
    Description = "Skip the files and folders matching the pattern (.gitignore syntax, relative to the current directory). Can be specified multiple times",
};
var gitTrackedOnlyOption = new Option<bool>("--git-tracked-only")
{
    Description = "Only process files tracked by Git. Untracked and ignored files are skipped",
};

RootCommand rootCommand = new("Update references");
rootCommand.Arguments.Add(pathsArgument);
rootCommand.Options.Add(recurseOption);
rootCommand.Options.Add(endOfLineOption);
rootCommand.Options.Add(checkOption);
rootCommand.Options.Add(excludeOption);
rootCommand.Options.Add(gitTrackedOnlyOption);
rootCommand.SetAction(async (result, cancellationToken) =>
{
    var recurse = result.CommandResult.GetRequiredValue(recurseOption);
    var paths = result.CommandResult.GetRequiredValue(pathsArgument);
    var eolOptionValue = result.CommandResult.GetRequiredValue(endOfLineOption);
    var check = result.CommandResult.GetValue(checkOption);
    var exclusions = new FileExclusions(result.CommandResult.GetValue(excludeOption) ?? []);
    var gitTrackedOnly = result.CommandResult.GetValue(gitTrackedOnlyOption);
    var hasErrors = false;
    var hasOutdatedFiles = false;

    IEnumerable<string> files;
    if (gitTrackedOnly)
    {
        var trackedFiles = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                trackedFiles.AddRange(await GitTrackedFiles.GetFilesAsync(path, recurse, cancellationToken));
            }
            catch (GitException ex)
            {
                Console.Error.WriteLine($"Error: cannot list the files tracked by Git in {path}: {ex.Message}");
                hasErrors = true;
            }
        }

        files = trackedFiles.Where(file => !exclusions.IsExcluded(file, PathItemType.File));
    }
    else
    {
        files = exclusions.GetFiles(paths, recurse);
    }

    var parallelOptions = new ParallelOptions
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount,
        CancellationToken = cancellationToken,
    };
    await Parallel.ForEachAsync(files, parallelOptions, async (file, cancellationToken) =>
    {
        if (Path.GetFileName(file) == "FileReferences.json")
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var fileReferences = await JsonSerializer.DeserializeAsync(stream, SourceGenerationContext.Default.FileReferences, cancellationToken: cancellationToken);
                if (fileReferences?.References is not null)
                {
                    foreach (var (fileName, fileReference) in fileReferences.References)
                    {
                        if (fileReference?.Ref is not null)
                        {
                            byte[] fileContent;
                            try
                            {
                                fileContent = await FileDownloader.DownloadFileRawAsync(file, fileReference.Ref, cancellationToken);
                            }
                            catch (ReferenceException ex)
                            {
                                // Keep the existing file
                                Console.Error.WriteLine($"Error in file {file}: cannot update file {fileName} with reference {fileReference.Ref}: {ex.Message}");
                                hasErrors = true;
                                continue;
                            }


                            var localFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file) ?? Environment.CurrentDirectory, fileName));
                            if (File.Exists(localFullPath) && (await File.ReadAllBytesAsync(localFullPath, cancellationToken)).AsSpan().SequenceEqual(fileContent))
                            {
                                Console.WriteLine($"File {fileName} is already up to date");
                            }
                            else if (check)
                            {
                                Console.Error.WriteLine($"File {localFullPath} is not up to date with reference {fileReference.Ref}");
                                hasOutdatedFiles = true;
                            }
                            else
                            {
                                Console.WriteLine($"Updating file {fileName} with reference {fileReference.Ref}");
                                await File.WriteAllBytesAsync(localFullPath, fileContent, CancellationToken.None);
                            }
                        }
                    }
                }

                return;
            }
            catch
            {
            }
        }

        const long candidateScanThreshold = 64 * 1024;
        if (new FileInfo(file).Length >= candidateScanThreshold &&
            !await FileUtilities.ContainsReferenceCandidateAsync(file, XmlDocReferenceUpdater.IsSupported(file), cancellationToken))
        {
            return;
        }

        var content = File.ReadAllText(file);
        var hasCommentReferences = content.Contains("ref:", StringComparison.OrdinalIgnoreCase) || content.Contains("reference:", StringComparison.OrdinalIgnoreCase);
        var hasXmlDocReferences = XmlDocReferenceUpdater.IsSupported(file) && content.Contains(XmlDocReferenceUpdater.NamespaceUri, StringComparison.Ordinal);
        if (!hasCommentReferences && !hasXmlDocReferences)
            return;

        if (Parser.TryGet(file, out var parser))
        {
            var sb = new StringBuilder(hasCommentReferences ? content.Length : 0);
            var isUpdated = false;
            ReferenceMatch? match = null;
            var matchEol = "";
            var originalContent = new StringBuilder();
            if (hasCommentReferences)
            {
                foreach (var (line, eol) in content.SplitLines())
                {
                    if (match is not null)
                    {
                        if (!parser.MatchEnd(line))
                        {
                            originalContent.Append(line).Append(eol);
                            continue;
                        }

                        try
                        {
                            sb.Append(await ReferenceRenderer.RenderAsync(file, match, matchEol, eolOptionValue, cancellationToken));
                            isUpdated = true;
                        }
                        catch (ReferenceException ex)
                        {
                            // Keep the existing content of the reference
                            Console.Error.WriteLine($"Error in file {file}: cannot update reference {match.Reference}: {ex.Message}");
                            hasErrors = true;
                            sb.Append(originalContent);
                        }

                        match = null;
                    }
                    else
                    {
                        match = parser.MatchStart(line);
                        if (match is not null)
                        {
                            Console.WriteLine($"Found start reference: {match.Reference} in file {file}");
                            matchEol = eol;
                            originalContent.Clear();
                        }
                    }

                    sb.Append(line).Append(eol);
                }
            }

            if (match is not null)
            {
                Console.Error.WriteLine($"Error in file {file}: cannot update reference {match.Reference}: the end marker is missing");
                hasErrors = true;
                sb.Append(originalContent);
            }

            var newContent = isUpdated ? sb.ToString() : content;
            if (XmlDocReferenceUpdater.IsSupported(file))
            {
                var xmlDocResult = await XmlDocReferenceUpdater.UpdateAsync(file, newContent, eolOptionValue, cancellationToken);
                if (xmlDocResult.HasErrors)
                {
                    hasErrors = true;
                }

                if (xmlDocResult.ReferenceCount > 0)
                {
                    newContent = xmlDocResult.Content;
                    isUpdated = true;
                }
            }

            if (isUpdated)
            {
                if (newContent != content)
                {
                    if (check)
                    {
                        Console.Error.WriteLine($"File {file} is not up to date");
                        hasOutdatedFiles = true;
                    }
                    else
                    {
                        Console.WriteLine($"Updating file {file}");
                        await File.WriteAllTextAsync(file, newContent, FileUtilities.GetEncoding(file), CancellationToken.None);
                    }
                }
                else
                {
                    Console.WriteLine($"File {file} is already up to date");
                }
            }
        }
    });

    return hasErrors || hasOutdatedFiles ? 1 : 0;
});

ParseResult parseResult = rootCommand.Parse(args);
return parseResult.Invoke();

public partial class Program;
