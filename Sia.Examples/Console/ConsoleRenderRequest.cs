#if !BROWSER
using Sia.Examples.Console.Layout;

namespace Sia.Examples.Console;

internal readonly record struct ConsoleRenderRequest(
    ConsoleDomNode Sidebar,
    ConsoleDomNode ContentHeader,
    ConsoleDomNode Notebook,
    ConsoleDomNode? Focused,
    Pane ActivePane,
    string? Error,
    int Width,
    int Height,
    EditMode EditMode,
    EditCursor? Cursor);
#endif
