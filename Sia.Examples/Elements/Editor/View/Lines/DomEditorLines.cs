using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed class DomEditorLines(
    DomElement gutter, DomElement lines, DomElement gutterAfter, DomElement linesAfter) : IEditorLinesOwner
{
    private readonly DomElement _gutter = gutter;
    private readonly DomElement _lines = lines;
    private readonly DomElement _gutterAfter = gutterAfter;
    private readonly DomElement _linesAfter = linesAfter;
    private readonly Dictionary<int, LineNode> _lineNodes = [];
    private (LineNode Lower, LineNode? Upper)? _placement;
    private int? _activeLineIdentity;
    private bool _selectionNeedsUpdate;
    private bool _disposed;

    public bool Apply(in EditorLinesView view, IReadOnlySet<int> identities)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _selectionNeedsUpdate = false;
        foreach (var identity in _lineNodes.Keys.Where(key => !identities.Contains(key)).ToArray()) RemoveLine(identity);
        foreach (var line in view.Lines.Span) {
            if (!_lineNodes.TryGetValue(line.Identity, out var node)
                || node.Index != line.Index || !node.View.Equals(line)) UpsertLine(line);
        }
        return _selectionNeedsUpdate;
    }

    public void Restore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var node in _lineNodes.Values.OrderBy(static node => node.Index)) {
            RenderLine(node, node.View);
            RenderContent(node.Content, node.View.Text, node.View.StyledRuns);
            node.Gutter.ToggleClass("active", _activeLineIdentity == node.View.Identity);
            _gutter.InsertBefore(node.Gutter, _gutterAfter);
            _lines.InsertBefore(node.Content, _linesAfter);
        }
    }

    public void SetActiveLine(int? identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeLineIdentity == identity) return;
        if (_activeLineIdentity is { } previous && _lineNodes.TryGetValue(previous, out var old)) old.Gutter.ToggleClass("active", false);
        if (identity is { } active && _lineNodes.TryGetValue(active, out var current)) current.Gutter.ToggleClass("active", true);
        _activeLineIdentity = identity;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearLineNodes();
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { ClearLineNodes(); } finally { _disposed = true; }
    }

    private void ClearLineNodes()
    {
        _placement = null;
        var nodes = _lineNodes.Values.ToArray();
        _lineNodes.Clear();
        List<Exception>? failures = null;
        foreach (var node in nodes) {
            try { node.Dispose(); }
            catch (Exception error) {
                (failures ??= []).Add(error);
                try { node.ReleaseHandles(); } catch (Exception releaseError) { failures.Add(releaseError); }
            }
        }
        if (failures is not null) throw new AggregateException(failures);
    }

    private void UpsertLine(in EditorLineView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var placement = _placement;
        _placement = null;
        var requiresPlacement = false;
        if (!_lineNodes.TryGetValue(view.Identity, out var node)) {
            node = new LineNode();
            _lineNodes.Add(view.Identity, node);
            node.Initialize();
            requiresPlacement = true;
        }

        if (placement is { } previous
            && (ReferenceEquals(previous.Lower, node) || ReferenceEquals(previous.Upper, node))) {
            placement = null;
        }
        LineNode? next = null;
        var moved = requiresPlacement || node.Index != view.Index;
        if (moved) {
            next = FindNext(view.Index, view.Identity, placement);
            _lines.InsertBefore(node.Content, next?.Content ?? _linesAfter);
            _gutter.InsertBefore(node.Gutter, next?.Gutter ?? _gutterAfter);
        }

        RenderLine(node, view);
        if (requiresPlacement) node.Gutter.ToggleClass("active", _activeLineIdentity == view.Identity);

        RenderContent(node.Content, view.Text, view.StyledRuns);
        _selectionNeedsUpdate = true;
        node.Index = view.Index;
        node.View = view;
        _placement = moved ? (node, next) : placement;
    }

    private void RemoveLine(int identity)
    {
        if (_lineNodes.TryGetValue(identity, out var node)) {
            if (_placement is { } placement
                && (ReferenceEquals(placement.Lower, node) || ReferenceEquals(placement.Upper, node))) {
                _placement = null;
            }
            node.Dispose();
            _lineNodes.Remove(identity);
            _selectionNeedsUpdate = true;
        }
    }

    private LineNode? FindNext(int index, int identity, (LineNode Lower, LineNode? Upper)? placement)
    {
        if (placement is { } previous && previous.Lower.Index <= index
            && (previous.Upper is null || previous.Upper.Index > index)) {
            return previous.Upper;
        }
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
        DomElement line,
        string text,
        IReadOnlyList<StyledRun>? styledRuns)
    {
        if (styledRuns is null) {
            line.Text(text);
            return;
        }

        line.TextRuns([..styledRuns.Select(static run => new DomTextRun(
            run.Text, run.Class is { } classification ? CSharpHighlighter.CssClass(classification) : null))]);
    }

    private static void RenderLine(LineNode node, in EditorLineView view)
    {
        node.Content.Attr("data-ln", view.Index.ToString());
        node.Gutter.Text((view.Index + 1).ToString());
        node.Content.Attr(
            "class",
            view.ClassName is null ? "editor-line" : $"editor-line {view.ClassName}");
    }

    private sealed class LineNode : IDisposable
    {
        public DomElement Content { get; private set; } = null!;
        public DomElement Gutter { get; private set; } = null!;
        public int Index { get; set; } = int.MaxValue;
        public EditorLineView View { get; set; }

        public void Initialize()
        {
            Content = DomElement.Create("div");
            Content.Class("editor-line");
            Gutter = DomElement.Create("div");
            Gutter.Class("editor-gutter-line");
        }

        public void Dispose()
        {
            Content?.Remove();
            Gutter?.Remove();
            Content?.Dispose();
            Gutter?.Dispose();
        }

        public void ReleaseHandles()
        {
            try { Content?.Dispose(); } finally { Gutter?.Dispose(); }
        }
    }
}
