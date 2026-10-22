#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia.Examples.Notebook;

namespace Sia.Examples.Browser;

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

        // Promise<byte[]> is unsupported by the interop generator; own the carrier until the copy completes.
        using var resource = await FetchBytesCoreAsync(url);

        _mainThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        return resource.GetPropertyAsByteArray("bytes")!;
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

    [JSImport("fetchBytes", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Object>>]
    private static partial Task<JSObject> FetchBytesCoreAsync(string url);

    [JSImport("fetchText", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> FetchTextCoreAsync(string url);
}
#endif
