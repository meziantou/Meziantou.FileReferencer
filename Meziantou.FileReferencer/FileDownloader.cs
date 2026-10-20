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

    /// <exception cref="ReferenceException">The file does not exist or cannot be downloaded</exception>
    public static Task<string> DownloadFileAsync(string filePath, string url, CancellationToken cancellationToken = default)
    {
        return GetContentAsync(_downloadTasks, filePath, url, uri => DownloadFile(uri, cancellationToken), path => File.ReadAllTextAsync(path, cancellationToken));
    }

    /// <exception cref="ReferenceException">The file does not exist or cannot be downloaded</exception>
    public static Task<byte[]> DownloadFileRawAsync(string filePath, string url, CancellationToken cancellationToken = default)
    {
        return GetContentAsync(_downloadRawTasks, filePath, url, uri => DownloadFileRaw(uri, cancellationToken), path => File.ReadAllBytesAsync(path, cancellationToken));
    }

    private static async Task<T> GetContentAsync<T>(ConcurrentDictionary<string, Lazy<Task<T>>> cache, string filePath, string url, Func<Uri, Task<T>> download, Func<string, Task<T>> read)
    {
        if (TryGetRemoteUri(url, out var uri))
        {
            try
            {
                return await cache.GetOrAdd(uri.AbsoluteUri, _ => new(() => download(uri))).Value;
            }
            catch (HttpRequestException ex)
            {
                throw new ReferenceException($"Cannot download '{uri}': {ex.Message}", ex);
            }
        }

        var fullPath = GetLocalPath(filePath, url);
        try
        {
            return await cache.GetOrAdd(fullPath, _ => new(() => read(fullPath))).Value;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ReferenceException($"The file '{fullPath}' does not exist.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ReferenceException($"Cannot read the file '{fullPath}': {ex.Message}", ex);
        }
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
