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
    private readonly DomElement _gutterBefore;
    private readonly DomElement _gutterAfter;
    private readonly DomElement _linesBefore;
    private readonly DomElement _linesAfter;
    private readonly DomElement _status;
    private readonly DomElement _documentSize;
    private readonly DomElement _cursorPosition;
    private readonly Dictionary<int, LineNode> _lineNodes = [];

    private EditorSelectionView? _selection;
    private int? _activeLineIdentity;
    private int? _preservedNativeEdit;
    private bool _suppressSelectionUpdate;
    private bool _disposed;
    private double _spacerBeforePx = -1;
    private double _spacerAfterPx = -1;

    public BrowserEditorView(string cellId, DomElement container)
    {
        _cellId = cellId;
        _container = container;
        _container.Class("editor-container");

        _gutter = DomElement.Create("div").Class("editor-gutter");
        _lines = DomElement.Create("div")
            .Class("editor-lines")
            .Attr("contenteditable", "true")
            .Attr("spellcheck", "false")
            .Attr("autocorrect", "off")
            .Attr("autocapitalize", "off");
        _scroll = DomElement.Create("div").Class("editor-scroll");
        _status = DomElement.Create("div").Class("editor-status");
        _documentSize = DomElement.Create("span");
        _cursorPosition = DomElement.Create("span");

        _gutterBefore = CreateSpacer();
        _gutterAfter = CreateSpacer();
        _linesBefore = CreateSpacer();
        _linesAfter = CreateSpacer();
        _gutter.Append(_gutterBefore).Append(_gutterAfter);
        _lines.Append(_linesBefore).Append(_linesAfter);

        _scroll.Append(_gutter).Append(_lines);
        _status.Append(_documentSize).Append(_cursorPosition);
        _container.Append(_scroll).Append(_status);
        DomRuntime.AttachEditorSurface(_cellId, _lines);
        DomRuntime.AttachGutterHeights(_gutter, _lines, _cellId);
    }

    private static DomElement CreateSpacer()
        => DomElement.Create("div")
            .Class("editor-spacer")
            .Attr("contenteditable", "false")
            .Attr("aria-hidden", "true");

    public void SuppressNextSelectionUpdate() => _suppressSelectionUpdate = true;

    public void PreserveNativeEdit(int lineIdentity) => _preservedNativeEdit = lineIdentity;

    public void ScrollLineIntoView(double targetTop) => DomRuntime.ScrollLineIntoView(_lines, targetTop);

    public void SetSpacerHeights(double beforePx, double afterPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!AlmostEqual(beforePx, _spacerBeforePx)) {
            _spacerBeforePx = beforePx;
            SetHeight(_gutterBefore, beforePx);
            SetHeight(_linesBefore, beforePx);
        }
        if (!AlmostEqual(afterPx, _spacerAfterPx)) {
            _spacerAfterPx = afterPx;
            SetHeight(_gutterAfter, afterPx);
            SetHeight(_linesAfter, afterPx);
        }
    }

    private static bool AlmostEqual(double a, double b) => Math.Abs(a - b) < 0.5;

    private static void SetHeight(DomElement element, double px)
        => element.Attr("style", $"height:{Math.Max(0, px).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px");

    public void RestoreDocumentStructure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gutter.Text(string.Empty);
        _lines.Text(string.Empty);
        _gutter.Append(_gutterBefore);
        _lines.Append(_linesBefore);
        foreach (var node in _lineNodes.Values.OrderBy(static node => node.Index)) {
            _gutter.Append(node.Gutter);
            _lines.Append(node.Content);
        }
        _gutter.Append(_gutterAfter);
        _lines.Append(_linesAfter);
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
            _lines.InsertBefore(node.Content, next?.Content ?? _linesAfter);
            _gutter.InsertBefore(node.Gutter, next?.Gutter ?? _gutterAfter);
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

    void IRenderHost<EditorStatusView>.Remove(in EditorStatusView view) { }

    void IRenderHost<EditorDocumentView>.Upsert(in EditorDocumentView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DomRuntime.SetDocumentLines(_lines, view.TotalLines);
    }

    void IRenderHost<EditorDocumentView>.Remove(in EditorDocumentView view) { }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        DomRuntime.DetachGutterHeights(_lines);
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
        _gutterBefore.Dispose();
        _gutterAfter.Dispose();
        _linesBefore.Dispose();
        _linesAfter.Dispose();
        _lines.Dispose();
        _gutter.Dispose();
        _scroll.Dispose();
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
