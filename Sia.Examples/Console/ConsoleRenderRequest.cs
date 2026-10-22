#if !BROWSER
using Sia.Examples.Console.Layout;

namespace Sia.Examples.Console;

internal readonly record struct ConsoleRenderRequest(
    ConsoleDomNode Sidebar,
    ConsoleDomNode Content,
    ConsoleDomNode? Focused,
    Pane ActivePane,
    string? Error,
    int Width,
    int Height,
    EditMode EditMode,
    EditCursor? Cursor);
#endif
