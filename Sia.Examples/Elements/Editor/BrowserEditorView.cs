using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorView : IEditorView
{
    private readonly string _cellId;
    private readonly DomElement _container;
    private readonly DomElement _gutter;
    private readonly DomElement _scroll;
    private readonly DomElement _lines;
    private readonly DomElement _status;
    private readonly DomElement _documentSize;
    private readonly DomElement _cursorPosition;
    private readonly Dictionary<int, LineNode> _lineNodes = [];

    private EditorSelectionView? _selection;
    private int? _activeLineIdentity;
    private int? _preservedNativeEdit;
    private bool _suppressSelectionUpdate;
    private bool _disposed;

    public BrowserEditorView(string cellId, DomElement container)
    {
        _cellId = cellId;
        _container = container;
        _container.Class("editor-container");

        _gutter = DomElement.Create("div").Class("editor-gutter");
        _scroll = DomElement.Create("div").Class("editor-scroll");
        _lines = DomElement.Create("div")
            .Class("editor-lines")
            .Attr("contenteditable", "true")
            .Attr("spellcheck", "false")
            .Attr("autocorrect", "off")
            .Attr("autocapitalize", "off");
        _status = DomElement.Create("div").Class("editor-status");
        _documentSize = DomElement.Create("span");
        _cursorPosition = DomElement.Create("span");

        _scroll.Append(_lines);
        _status.Append(_documentSize).Append(_cursorPosition);
        _container.Append(_gutter).Append(_scroll).Append(_status);
        DomRuntime.SyncGutterScroll(_scroll, _gutter);
        DomRuntime.AttachEditorSurface(_cellId, _lines);
    }

    public void SuppressNextSelectionUpdate() => _suppressSelectionUpdate = true;

    public void PreserveNativeEdit(int lineIdentity) => _preservedNativeEdit = lineIdentity;

    public void RestoreDocumentStructure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lines.Text(string.Empty);
        foreach (var node in _lineNodes.Values.OrderBy(static node => node.Index)) {
            _lines.Append(node.Content);
        }
    }

    public void MountOverlay(DomElement overlay, int lineIndex, int column)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _container.Append(overlay);
        PlaceOverlay(overlay, lineIndex, column);
    }

    public void PlaceOverlay(DomElement overlay, int lineIndex, int column)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DomRuntime.PlaceOverlay(_container, _lines, overlay, lineIndex, column);
    }

    void IRenderHost<EditorLineView>.Upsert(in EditorLineView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requiresPlacement = false;
        if (!_lineNodes.TryGetValue(view.Identity, out var node)) {
            node = LineNode.Create();
            _lineNodes.Add(view.Identity, node);
            requiresPlacement = true;
        }

        if (requiresPlacement || node.Index != view.Index) {
            var next = FindNext(view.Index, view.Identity);
            _lines.InsertBefore(node.Content, next?.Content);
            _gutter.InsertBefore(node.Gutter, next?.Gutter);
        }

        node.Content.Attr("data-ln", view.Index.ToString());
        node.Gutter.Text((view.Index + 1).ToString());
        node.Content.Attr(
            "class",
            view.ClassName is null ? "editor-line" : $"editor-line {view.ClassName}");

        var preserveNativeEdit = _preservedNativeEdit == view.Identity
            && node.Content.TextContent() == view.Text;
        if (_preservedNativeEdit == view.Identity) {
            _preservedNativeEdit = null;
        }
        if (!preserveNativeEdit) {
            RenderContent(node.Content, view.Text, view.StyledRuns);
            RestoreSelectionAfterRender(view.Index);
        }
        node.Index = view.Index;
    }

    void IRenderHost<EditorLineView>.Remove(in EditorLineView view)
    {
        if (_lineNodes.Remove(view.Identity, out var node)) {
            node.Dispose();
        }
    }

    void IRenderHost<EditorActiveLineView>.Upsert(in EditorActiveLineView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeLineIdentity == view.Identity) {
            return;
        }
        if (_activeLineIdentity is { } previousIdentity
            && _lineNodes.TryGetValue(previousIdentity, out var previous)) {
            previous.Gutter.ToggleClass("active", false);
        }
        if (_lineNodes.TryGetValue(view.Identity, out var node)) {
            node.Gutter.ToggleClass("active", true);
        }
        _activeLineIdentity = view.Identity;
    }

    void IRenderHost<EditorActiveLineView>.Remove(in EditorActiveLineView view)
    {
        if (_activeLineIdentity != view.Identity) {
            return;
        }
        if (_lineNodes.TryGetValue(view.Identity, out var node)) {
            node.Gutter.ToggleClass("active", false);
        }
        _activeLineIdentity = null;
    }

    void IRenderHost<EditorSelectionView>.Upsert(in EditorSelectionView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _selection = view;
        if (_suppressSelectionUpdate) {
            _suppressSelectionUpdate = false;
            return;
        }
        DomRuntime.SetEditorSelection(
            _lines,
            view.AnchorLineIndex,
            view.AnchorColumn,
            view.HeadLineIndex,
            view.HeadColumn);
    }

    void IRenderHost<EditorSelectionView>.Remove(in EditorSelectionView view)
    {
        _selection = null;
    }

    void IRenderHost<EditorStatusView>.Upsert(in EditorStatusView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _documentSize.Text(view.DocumentSize);
        _cursorPosition.Text(view.CursorPosition);
    }

    void IRenderHost<EditorStatusView>.Remove(in EditorStatusView view)
    {
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        DomRuntime.DetachEditorSurface(_cellId, _lines);
        foreach (var node in _lineNodes.Values) {
            node.Dispose();
        }
        _lineNodes.Clear();
        _selection = null;
        _activeLineIdentity = null;
        _status.Dispose();
        _cursorPosition.Dispose();
        _documentSize.Dispose();
        _lines.Dispose();
        _scroll.Dispose();
        _gutter.Dispose();
        _container.ToggleClass("editor-container", false);
    }

    private LineNode? FindNext(int index, int identity)
    {
        LineNode? result = null;
        foreach (var (candidateIdentity, candidate) in _lineNodes) {
            if (candidateIdentity == identity || candidate.Index <= index) {
                continue;
            }
            if (result is null || candidate.Index < result.Index) {
                result = candidate;
            }
        }
        return result;
    }

    private void RestoreSelectionAfterRender(int lineIndex)
    {
        if (_selection is not { } selection
            || selection.AnchorLineIndex != lineIndex
                && selection.HeadLineIndex != lineIndex) {
            return;
        }
        DomRuntime.SetEditorSelection(
            _lines,
            selection.AnchorLineIndex,
            selection.AnchorColumn,
            selection.HeadLineIndex,
            selection.HeadColumn);
    }

    private static void RenderContent(
        DomElement line,
        string text,
        IReadOnlyList<StyledRun>? styledRuns)
    {
        if (styledRuns is null) {
            line.Text(text);
            return;
        }

        line.Text(string.Empty);
        foreach (var run in styledRuns) {
            if (run.Class is null) {
                using var textNode = DomElement.CreateText(run.Text);
                line.Append(textNode);
                continue;
            }

            using var span = DomElement.Create("span")
                .Class(CSharpHighlighter.CssClass(run.Class))
                .Text(run.Text);
            line.Append(span);
        }
    }

    private sealed class LineNode(DomElement content, DomElement gutter) : IDisposable
    {
        public DomElement Content { get; } = content;

        public DomElement Gutter { get; } = gutter;

        public int Index { get; set; } = int.MaxValue;

        public static LineNode Create()
            => new(
                DomElement.Create("div").Class("editor-line"),
                DomElement.Create("div").Class("editor-gutter-line"));

        public void Dispose()
        {
            Content.Remove();
            Gutter.Remove();
            Content.Dispose();
            Gutter.Dispose();
        }
    }
}
