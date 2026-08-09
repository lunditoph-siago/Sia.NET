#if !BROWSER
using Sia_Examples.Console.Layout;

namespace Sia_Examples.Console;

internal sealed partial class ConsoleDomBackend
{
    private readonly ConsoleDomRenderer _renderer = new();

    private Pane _activePane = Pane.Sidebar;
    private ConsoleDomNode? _focused;
    private string? _error;
    private bool _dirty = true;

    public void ReportError(string message)
    {
        _error = message;
        MarkChanged();
    }

    public void Flush()
    {
        ThrowIfDisposed();
        NormalizeFocus();
        if (!_dirty) {
            return;
        }
        var rows = _renderer.Render(new(
            _sidebar,
            _contentHeader,
            _notebook,
            _focused,
            _activePane,
            _error,
            _terminal.Width,
            _terminal.Height,
            _editMode,
            ResolveEditCursor()));
        _terminal.Draw(rows);
        _dirty = false;
    }

    private ConsoleDomNode[] InteractiveNodes(Pane pane)
        => pane switch {
            Pane.Sidebar => Interactive(_sidebar),
            _ => [.. Interactive(_contentHeader), .. Interactive(_notebook)],
        };

    private static ConsoleDomNode[] Interactive(ConsoleDomNode root)
        => root.DescendantsAndSelf()
            .Where(static node => node.IsVisible
                && (node.Listeners.ContainsKey("click") || node.HasClass("editor-surface")))
            .ToArray();

    private void NormalizeFocus()
    {
        var nodes = InteractiveNodes(_activePane);
        if (nodes.Length == 0) {
            _focused = null;
        } else if (_focused is null || !nodes.Contains(_focused)) {
            _focused = nodes[0];
        }
    }

    private void MoveFocus(int offset)
    {
        var nodes = InteractiveNodes(_activePane);
        if (nodes.Length == 0) {
            return;
        }
        var index = _focused is null ? -1 : Array.IndexOf(nodes, _focused);
        _focused = nodes[(index + offset + nodes.Length) % nodes.Length];
        MarkChanged();
    }

    private void MoveFocusToBoundary(bool first)
    {
        var nodes = InteractiveNodes(_activePane);
        if (nodes.Length == 0) {
            return;
        }
        _focused = first ? nodes[0] : nodes[^1];
        MarkChanged();
    }

    private Pane PaneOf(ConsoleDomNode node)
        => _sidebar.Contains(node) ? Pane.Sidebar : Pane.Content;

    private void SwitchPane(Pane pane)
    {
        if (_activePane == pane) {
            return;
        }
        _activePane = pane;
        _focused = null;
        NormalizeFocus();
        MarkChanged();
    }

    private void TogglePane()
        => SwitchPane(_activePane == Pane.Sidebar ? Pane.Content : Pane.Sidebar);

    private string? ActivateFocused()
    {
        NormalizeFocus();
        return _focused?.Listeners.GetValueOrDefault("click");
    }

    private void MarkChanged() => _dirty = true;
}
#endif
