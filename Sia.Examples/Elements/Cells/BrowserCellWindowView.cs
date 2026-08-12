using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class BrowserCellWindowView : IDisposable
{
    public BrowserCellWindowView(
        CellWindow window,
        DomElement content,
        DomElement? toolbar = null)
    {
        Window = window;
        Content = content
            .Id(window.Id)
            .Class("window")
            .Attr("role", "tabpanel")
            .Attr("aria-label", window.Title);
        Toolbar = toolbar;
    }

    public CellWindow Window { get; }

    public DomElement Content { get; }

    public DomElement? Toolbar { get; }

    public void Dispose()
    {
        Toolbar?.Remove();
        Content.Remove();
        Toolbar?.Dispose();
        Content.Dispose();
    }
}
