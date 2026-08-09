#if !BROWSER
using Sia_Examples.Dom;

namespace Sia_Examples.Console;

internal sealed partial class ConsoleDomBackend
{
    public void AttachEditorSurface(string cellId, IDomNode node)
    {
        SetAttribute(node, "data-editor", cellId);
        ToggleClass(node, "editor-surface", enabled: true);
    }

    public void DetachEditorSurface(string cellId, IDomNode node)
    {
        var target = AsNode(node);
        if (target.Attributes.GetValueOrDefault("data-editor") != cellId) {
            return;
        }
        target.Attributes.Remove("data-editor");
        target.Classes.Remove("editor-surface");
        MarkChanged();
    }

    public void AcknowledgeEditorCommand(string cellId, int sequence)
    {
    }

    public void SetEditorSelection(
        IDomNode node,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn)
    {
        SetAttribute(
            node,
            "data-selection",
            $"{anchorLine}:{anchorColumn}:{headLine}:{headColumn}");
    }

    public void SyncGutterScroll(IDomNode scroll, IDomNode gutter)
    {
        _ = AsNode(scroll);
        _ = AsNode(gutter);
    }

    public void PlaceOverlay(
        IDomNode container,
        IDomNode surface,
        IDomNode overlay,
        int lineIndex,
        int column)
    {
        _ = AsNode(container);
        _ = AsNode(surface);
        SetAttribute(overlay, "data-overlay", $"{lineIndex}:{column}");
    }

    public void ClearOverlayPlacement(IDomNode overlay)
    {
        var target = AsNode(overlay);
        if (target.Attributes.Remove("data-overlay")) {
            MarkChanged();
        }
    }

    public void EnsureVisible(IDomNode container, IDomNode element)
    {
        _ = AsNode(container);
        var target = AsNode(element);
        if (target.Listeners.ContainsKey("click")) {
            _activePane = PaneOf(target);
            _focused = target;
            MarkChanged();
        }
    }
}
#endif
