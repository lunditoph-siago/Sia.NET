using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

public sealed class BrowserDockWindowView : IDisposable
{
    public BrowserDockWindowView(
        DockWindow window,
        DomElement content,
        DomElement? toolbar = null)
    {
        Window = window;
        Content = content
            .Id(window.Id)
            .Class("dock-window")
            .Attr("role", "tabpanel")
            .Attr("aria-label", window.Title);
        Toolbar = toolbar;
    }

    public DockWindow Window { get; }

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
