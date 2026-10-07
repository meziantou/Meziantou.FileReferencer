using System.CommandLine;
using System.Text;
using System.Text.Json;
using Meziantou.FileReferencer;

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

RootCommand rootCommand = new("Update references");
rootCommand.Arguments.Add(pathsArgument);
rootCommand.Options.Add(recurseOption);
rootCommand.Options.Add(endOfLineOption);
rootCommand.SetAction(async (result, cancellationToken) =>
{
    var recurse = result.CommandResult.GetRequiredValue(recurseOption);
    var paths = result.CommandResult.GetRequiredValue(pathsArgument);
    var eolOptionValue = result.CommandResult.GetRequiredValue(endOfLineOption);
    var hasErrors = false;

    var parallelOptions = new ParallelOptions
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount,
        CancellationToken = cancellationToken,
    };
    await Parallel.ForEachAsync(GetAllFiles(paths, recurse), async (file, cancellationToken) =>
    {
        if (Path.GetFileName(file) == "FileReferences.json")
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var content = await JsonSerializer.DeserializeAsync(stream, SourceGenerationContext.Default.FileReferences, cancellationToken: cancellationToken);
                if (content?.References is not null)
                {
                    foreach (var (fileName, fileReference) in content.References)
                    {
                        if (fileReference?.Ref is not null)
                        {
                            Console.WriteLine($"Updating file {fileName} with reference {fileReference.Ref}");
                            var fileContent = await FileDownloader.DownloadFileRawAsync(file, fileReference.Ref, cancellationToken);

                            var localFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file) ?? Environment.CurrentDirectory, fileName));
                            await File.WriteAllBytesAsync(localFullPath, fileContent, CancellationToken.None);
                        }
                    }
                }

                return;
            }
            catch
            {
            }
        }

        var fileExtension = Path.GetExtension(file);
        if (Parser.TryGet(fileExtension, out var parser))
        {
            var content = File.ReadAllText(file);
            var sb = new StringBuilder(content.Length);
            var isUpdated = false;
            ReferenceMatch? match = null;
            var matchEol = "";
            var originalContent = new StringBuilder();
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
                    Console.WriteLine($"Updating file {file}");
                    await File.WriteAllTextAsync(file, newContent, FileUtilities.GetEncoding(file), CancellationToken.None);
                }
                else
                {
                    Console.WriteLine($"File {file} is already up to date");
                }
            }
        }
    });

    return hasErrors ? 1 : 0;
});

ParseResult parseResult = rootCommand.Parse(args);
return parseResult.Invoke();

static IEnumerable<string> GetAllFiles(string[] paths, bool recurse)
{
    foreach (var path in paths)
    {
        if (Directory.Exists(path))
        {
            foreach (var subFile in Directory.EnumerateFiles(path, "*.*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                yield return subFile;
            }
        }
        else if (File.Exists(path))
        {
            yield return path;
        }
    }
}

public partial class Program;
