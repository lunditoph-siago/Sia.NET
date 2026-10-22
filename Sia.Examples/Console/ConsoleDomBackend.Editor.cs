#if !BROWSER
using Sia.Examples.Dom;

namespace Sia.Examples.Console;

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
        _editorSequences.Remove(cellId);
        for (var remaining = _pendingEditorEvents.Count; remaining > 0; remaining--) {
            var command = _pendingEditorEvents.Dequeue();
            if (!ReferenceEquals(command.Surface, target)) {
                _pendingEditorEvents.Enqueue(command);
            }
        }
        if (ReferenceEquals(_editingSurface, target)) {
            ExitEditor();
        }
        MarkChanged();
    }

    public void AcknowledgeEditorCommand(string cellId, int sequence)
    {
    }

    public void ResolveEditorClipboard(string cellId, int requestId, string? text)
    {
    }

    public void PrepareEditorReconciliation(IDomNode node, int sequence)
    {
        _ = AsNode(node);
    }

    public string TakeEditorInput(IDomNode node, bool sourceBoundary)
    {
        _ = AsNode(node);
        return string.Empty;
    }

    public void AttachGutterHeights(IDomNode gutter, IDomNode lines, string cellId)
    {
        _ = AsNode(gutter);
        _ = AsNode(lines);
    }

    public void DetachGutterHeights(IDomNode lines)
    {
        _ = AsNode(lines);
    }

    public void SetDocumentLines(IDomNode lines, int totalLines)
    {
        _ = AsNode(lines);
    }

    public EditorScrollPosition? ReadEditorScrollPosition(IDomNode lines)
    {
        _ = AsNode(lines);
        return null;
    }

    public void RestoreEditorScroll(IDomNode lines, int generation, EditorScrollPosition position)
    {
        _ = AsNode(lines);
    }

    public void SetEditorSelection(
        IDomNode node,
        int anchorLine,
        int anchorColumn,
        int headLine,
        int headColumn,
        bool reveal)
    {
        SetAttribute(
            node,
            "data-selection",
            $"{anchorLine}:{anchorColumn}:{headLine}:{headColumn}");
    }

    public void PlaceOverlay(
        IDomNode container,
        IDomNode surface,
        IDomNode overlay,
        int lineIndex,
        int column,
        int endLineIndex = -1,
        int endColumn = -1)
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
        if (target.ActivationPayload is not null || target.HasClass("editor-surface")) {
            _activePane = PaneOf(target);
            _focused = target;
            MarkChanged();
        }
    }

    public void Focus(IDomNode node) => EnsureVisible(node, node);
}
#endif
