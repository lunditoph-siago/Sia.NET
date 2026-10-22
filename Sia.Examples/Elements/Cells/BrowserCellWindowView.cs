using Sia.Examples.Dom;

namespace Sia.Examples.Notebook;

public sealed class BrowserCellWindowView(
    CellWindow window,
    DomElement content,
    DomElement? toolbar = null) : IDisposable
{
    public CellWindow Window { get; } = window;

    public DomElement Content { get; } = content
        .Id(window.Id)
        .Class("window")
        .Attr("role", "tabpanel")
        .Attr("aria-label", window.Title);

    public DomElement? Toolbar { get; } = toolbar;

    public void Dispose()
    {
        Toolbar?.Remove();
        Content.Remove();
        Toolbar?.Dispose();
        Content.Dispose();
    }
}
