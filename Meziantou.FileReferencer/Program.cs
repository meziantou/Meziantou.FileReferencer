using System.CommandLine;
using System.Text;
using System.Text.Json;
using Meziantou.FileReferencer;

var pathsArgument = new Argument<string[]>("--path")
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
            foreach (var (line, eol) in content.SplitLines())
            {
                if (match is not null)
                {
                    if (parser.MatchEnd(line))
                    {
                        match = null;
                    }
                }
                else
                {
                    match = parser.MatchStart(line);
                    if (match is not null)
                    {
                        Console.WriteLine($"Found start reference: {match.Reference} in file {file}");

                        var remoteContent = await FileDownloader.DownloadFileAsync(file, match.Reference, cancellationToken);
                        sb.Append(line).Append(eol);

                        // Update end of line characters
                        switch (match.EndOfLine ?? eolOptionValue)
                        {
                            case EndOfLineOption.Auto:
                                remoteContent = remoteContent.ReplaceLineEndings(eol);
                                break;
                            case EndOfLineOption.Cr:
                                remoteContent = remoteContent.ReplaceLineEndings("\r");
                                break;
                            case EndOfLineOption.Lf:
                                remoteContent = remoteContent.ReplaceLineEndings("\n");
                                break;
                            case EndOfLineOption.CrLf:
                                remoteContent = remoteContent.ReplaceLineEndings("\r\n");
                                break;
                        }

                        // Trim extra new lines at the end of the file, keep the last one
                        if (match.TrimFinalEmptyLines ?? true)
                        {
                            remoteContent = remoteContent.TrimEnd('\r', '\n') + eol;
                        }

                        // Match indentation (e.g. json, yaml)
                        if (match.UpdateIndentation ?? true && !string.IsNullOrEmpty(match.Indentation))
                        {
                            var indentedContent = new StringBuilder();
                            var lines = remoteContent.SplitLines();
                            foreach (var (remoteLine, remoteEol) in lines)
                            {
                                if (string.IsNullOrWhiteSpace(remoteLine))
                                {
                                    indentedContent.Append(eol);
                                }
                                else
                                {
                                    indentedContent.Append(match.Indentation).Append(remoteLine).Append(eol);
                                }
                            }

                            remoteContent = indentedContent.ToString();
                        }

                        sb.Append(remoteContent);
                        isUpdated = true;
                        continue;
                    }
                }

                if (match is null)
                {
                    sb.Append(line).Append(eol);
                }
            }

            if (isUpdated)
            {
                var newContent = sb.ToString();
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
