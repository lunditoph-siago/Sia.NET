using System.Runtime.InteropServices.JavaScript;
using Sia_Examples.Notebook;

namespace Sia_Examples.Browser;

public sealed partial class BrowserResourceLoader : IResourceLoader
{
    private readonly BrowserMainThread _mainThread;

    public BrowserResourceLoader(BrowserMainThread mainThread)
    {
        _mainThread = mainThread;
    }

    public async ValueTask<byte[]> FetchBytesAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        _mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        var base64 = await FetchBase64Async(url);

        _mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.FromBase64String(base64);
    }

    public async ValueTask<string> FetchTextAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        _mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        var text = await FetchTextCoreAsync(url);

        _mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    [JSImport("fetchBase64", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> FetchBase64Async(string url);

    [JSImport("fetchText", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> FetchTextCoreAsync(string url);
}
