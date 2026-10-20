using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Meziantou.FileReferencer;
internal static class Git
{
    /// <summary>Runs git and captures its output.</summary>
    /// <exception cref="Win32Exception">git is not available.</exception>
    public static async Task<GitResult> RunAsync(string workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(psi) ?? throw new Win32Exception("Cannot start git");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new GitResult(process.ExitCode, await outputTask, await errorTask);
    }
}

internal sealed record GitResult(int ExitCode, string Output, string Error);
