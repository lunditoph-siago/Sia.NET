#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace Sia.Examples.Browser;

internal sealed partial class BrowserNavigation : IApplicationNavigation
{
    public BrowserNavigation() => InitialPath = Start();

    public string InitialPath { get; }
    public bool IsCurrent(int ticket) => IsCurrentCore(ticket);
    public Task HoldAsync(int ticket) => HoldCoreAsync(ticket);
    public Task CommitAsync(string path, int ticket = 0, bool replace = false)
        => CommitCoreAsync(path, ticket, replace);
    public void Dispose() => Stop();

    [JSImport("showNotFound", "main.js")]
    internal static partial void ShowNotFound();

    [JSImport("startNavigation", "main.js")]
    private static partial string Start();

    [JSImport("isCurrentNavigation", "main.js")]
    private static partial bool IsCurrentCore(int ticket);

    [JSImport("holdNavigation", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task HoldCoreAsync(int ticket);

    [JSImport("commitNavigation", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task CommitCoreAsync(string path, int ticket, bool replace);

    [JSImport("stopNavigation", "main.js")]
    private static partial void Stop();
}
#endif
