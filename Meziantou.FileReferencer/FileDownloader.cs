using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.FileReferencer;
internal static class FileDownloader
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> _downloadTasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _downloadRawTasks = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetRemoteUri(string url, [NotNullWhen(true)] out Uri? uri)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static string GetLocalPath(string filePath, string url)
    {
        var parent = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
        return Path.GetFullPath(Path.Combine(parent, url));
    }

    public static async Task<string> DownloadFileAsync(string filePath, string url, CancellationToken cancellationToken = default)
    {
        if (TryGetRemoteUri(url, out var uri))
            return await _downloadTasks.GetOrAdd(uri.AbsoluteUri, url => new(() => DownloadFile(uri, cancellationToken))).Value;

        var fullPath = GetLocalPath(filePath, url);

        return await _downloadTasks.GetOrAdd(fullPath, path => new(() => File.ReadAllTextAsync(fullPath, cancellationToken))).Value;
    }

    public static async Task<byte[]> DownloadFileRawAsync(string filePath, string url, CancellationToken cancellationToken = default)
    {
        if (TryGetRemoteUri(url, out var uri))
            return await _downloadRawTasks.GetOrAdd(uri.AbsoluteUri, url => new(() => DownloadFileRaw(uri, cancellationToken))).Value;

        var fullPath = GetLocalPath(filePath, url);

        return await _downloadRawTasks.GetOrAdd(fullPath, path => new(() => File.ReadAllBytesAsync(fullPath, cancellationToken))).Value;
    }

    private static async Task<string> DownloadFile(Uri url, CancellationToken cancellationToken)
    {
        using var request = await CreateRequest(url, cancellationToken);

        using var response = await SharedHttpClient.Instance.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task<byte[]> DownloadFileRaw(Uri url, CancellationToken cancellationToken)
    {
        using var request = await CreateRequest(url, cancellationToken);

        using var response = await SharedHttpClient.Instance.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static async Task<HttpRequestMessage> CreateRequest(Uri url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Meziantou.FileReferencer/1.0");

        // Try authenticate request
        if (string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase) || string.Equals(url.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            var token = Environment.GetEnvironmentVariable("GH_TOKEN");
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "gh",
                        Arguments = "auth token",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var process = Process.Start(psi);
                    if (process is not null)
                    {
                        await process.WaitForExitAsync(cancellationToken);
                        if (process.ExitCode == 0)
                        {
                            var tokenOutput = await process.StandardOutput.ReadToEndAsync();
                            if (!string.IsNullOrEmpty(tokenOutput))
                            {
                                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenOutput.Trim());
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        return request;
    }
}
