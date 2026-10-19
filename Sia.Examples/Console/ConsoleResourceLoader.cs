#if !BROWSER
using Sia.Examples.Notebook;

namespace Sia.Examples.Console;

internal sealed class ConsoleResourceLoader : IResourceLoader, IDisposable
{
    private readonly HttpClient _client = new();

    public async ValueTask<byte[]> FetchBytesAsync(
        string url,
        CancellationToken cancellationToken = default)
        => await _client.GetByteArrayAsync(url, cancellationToken);

    public async ValueTask<string> FetchTextAsync(
        string url,
        CancellationToken cancellationToken = default)
        => await _client.GetStringAsync(url, cancellationToken);

    public void Dispose() => _client.Dispose();
}
#endif
