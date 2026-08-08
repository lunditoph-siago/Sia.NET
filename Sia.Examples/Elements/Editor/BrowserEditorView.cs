using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorView : IEditorView
{
    private readonly string _cellId;
    private readonly BrowserElement _container;
    private readonly BrowserElement _gutter;
    private readonly BrowserElement _scroll;
    private readonly BrowserElement _lines;
    private readonly BrowserElement _status;
    private readonly BrowserElement _documentSize;
    private readonly BrowserElement _cursorPosition;
    private readonly Dictionary<int, LineNode> _lineNodes = [];

    private int? _activeLineIdentity;
    private int? _preservedNativeEdit;
    private bool _suppressSelectionUpdate;
    private bool _disposed;

    public BrowserEditorView(string cellId, BrowserElement container)
    {
        _cellId = cellId;
        _container = container;
        _container.Class("editor-container");

        _gutter = BrowserElement.Create("div").Class("editor-gutter");
        _scroll = BrowserElement.Create("div").Class("editor-scroll");
        _lines = BrowserElement.Create("div")
            .Class("editor-lines")
            .Attr("contenteditable", "true")
            .Attr("spellcheck", "false")
            .Attr("autocorrect", "off")
            .Attr("autocapitalize", "off");
        _status = BrowserElement.Create("div").Class("editor-status");
        _documentSize = BrowserElement.Create("span");
        _cursorPosition = BrowserElement.Create("span");

        _scroll.Append(_lines);
        _status.Append(_documentSize).Append(_cursorPosition);
        _container.Append(_gutter).Append(_scroll).Append(_status);
        BrowserDom.SyncGutterScroll(_scroll.Handle, _gutter.Handle);
        BrowserDom.AttachEditorSurface(_cellId, _lines.Handle);
    }

    public void SuppressNextSelectionUpdate() => _suppressSelectionUpdate = true;

    public void PreserveNativeEdit(int lineIdentity) => _preservedNativeEdit = lineIdentity;

    void IRenderHost<EditorLineView>.Upsert(in EditorLineView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_lineNodes.TryGetValue(view.Identity, out var node)) {
            node = LineNode.Create();
            _lineNodes.Add(view.Identity, node);
        }

        var next = FindNext(view.Index, view.Identity);
        _lines.InsertBefore(node.Content, next?.Content);
        _gutter.InsertBefore(node.Gutter, next?.Gutter);

        node.Content.Attr("data-ln", view.Index.ToString());
        node.Gutter.Text((view.Index + 1).ToString());
        node.Content.Attr(
            "class",
            view.ClassName is null ? "cm-line" : $"cm-line {view.ClassName}");

        var preserveNativeEdit = _preservedNativeEdit == view.Identity
            && node.Content.TextContent() == view.Text;
        if (_preservedNativeEdit == view.Identity) {
            _preservedNativeEdit = null;
        }
        if (!preserveNativeEdit) {
            RenderContent(node.Content, view.Text, view.StyledRuns);
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
        if (_suppressSelectionUpdate) {
            _suppressSelectionUpdate = false;
            return;
        }
        BrowserDom.SetEditorSelection(
            _lines.Handle,
            view.AnchorLineIndex,
            view.AnchorColumn,
            view.HeadLineIndex,
            view.HeadColumn);
    }

    void IRenderHost<EditorSelectionView>.Remove(in EditorSelectionView view)
    {
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
        BrowserDom.DetachEditorSurface(_cellId, _lines.Handle);
        foreach (var node in _lineNodes.Values) {
            node.Dispose();
        }
        _lineNodes.Clear();
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

    private static void RenderContent(
        BrowserElement line,
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
                using var textNode = BrowserElement.CreateText(run.Text);
                line.Append(textNode);
                continue;
            }

            using var span = BrowserElement.Create("span")
                .Class(CSharpHighlighter.CssClass(run.Class))
                .Text(run.Text);
            line.Append(span);
        }
    }

    private sealed class LineNode(BrowserElement content, BrowserElement gutter) : IDisposable
    {
        public BrowserElement Content { get; } = content;

        public BrowserElement Gutter { get; } = gutter;

        public int Index { get; set; } = int.MaxValue;

        public static LineNode Create()
            => new(
                BrowserElement.Create("div").Class("cm-line"),
                BrowserElement.Create("div").Class("editor-gutter-line"));

        public void Dispose()
        {
            Content.Remove();
            Gutter.Remove();
            Content.Dispose();
            Gutter.Dispose();
        }
    }
}
