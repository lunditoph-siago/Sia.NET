#if !BROWSER
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class HttpResourceLoader : IResourceLoader, IDisposable
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
