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
    private readonly IEditorLinesOwner _lineOwner;

    private EditorSelectionView? _selection;
    private int? _activeLineIdentity;
    private bool _suppressSelectionUpdate;
    private bool _disposed;
    private double _spacerBeforePx = -1;
    private double _spacerAfterPx = -1;
    private int _updateDepth;
    private EditorStatusView _statusView;
    private int _totalLines;
    private int _documentRevision;
    private bool _revealSelection;
    private bool _selectionNeedsUpdate;
    private bool _rebuildLines;

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
        _lineOwner = DomRuntime.Backend is IEditorLinesOwnerFactory factory
            ? factory.CreateEditorLines(_gutter, _lines, _gutterBefore, _gutterAfter, _linesBefore, _linesAfter)
            : new DomEditorLines(_gutter, _lines, _gutterAfter, _linesAfter);

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

    internal void PrepareReconciliation(int sequence)
        => DomRuntime.PrepareEditorReconciliation(_lines, sequence);

    internal string TakePendingInput(bool sourceBoundary = true) => DomRuntime.TakeEditorInput(_lines, sourceBoundary);

    internal void SetDocumentRevision(int revision)
    {
        _documentRevision = revision;
        _lines.Attr("data-editor-document-revision", revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal void BeginUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_updateDepth == 0) {
            DomRuntime.BeginUpdate(_lines);
        }
        _updateDepth++;
    }

    internal void EndUpdate()
    {
        if (_disposed && _updateDepth == 0) return;
        if (--_updateDepth == 0) {
            try {
                try {
                    if ((_revealSelection || _selectionNeedsUpdate) && _selection is { } selection) {
                        DomRuntime.SetEditorSelection(
                            _lines, selection.AnchorLineIndex, selection.AnchorColumn,
                            selection.HeadLineIndex, selection.HeadColumn, reveal: _revealSelection);
                    }
                }
                finally {
                    _revealSelection = false;
                    _selectionNeedsUpdate = false;
                    DomRuntime.EndUpdate(_lines);
                }
            }
            catch { _rebuildLines = true; throw; }
        }
    }

    internal void RevealSelection() => _revealSelection = true;

    internal void CancelSelectionReveal() => _revealSelection = false;

    internal EditorScrollPosition? ReadScrollPosition() => DomRuntime.ReadEditorScrollPosition(_lines);

    internal void RestoreScroll(int generation, EditorScrollPosition position)
        => DomRuntime.RestoreEditorScroll(_lines, generation, position);

    public void SetSpacerHeights(double beforePx, double afterPx)
    {
        if (DomRuntime.Backend.SupportsNativeInput) beforePx = afterPx = 0;
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
        if (_rebuildLines) ResetLines();
        _gutter.Text(string.Empty);
        _lines.Text(string.Empty);
        _gutter.Append(_gutterBefore);
        _lines.Append(_linesBefore);
        SetHeight(_gutterBefore, _spacerBeforePx);
        SetHeight(_linesBefore, _spacerBeforePx);
        SetHeight(_gutterAfter, _spacerAfterPx);
        SetHeight(_linesAfter, _spacerAfterPx);
        _gutter.Append(_gutterAfter);
        _lines.Append(_linesAfter);
        _lineOwner.Restore();
        _documentSize.Text(_statusView.DocumentSize);
        _cursorPosition.Text(_statusView.CursorPosition);
        DomRuntime.SetDocumentLines(_lines, _totalLines);
        SetDocumentRevision(_documentRevision);
        if (_selection is { } selection) {
            DomRuntime.SetEditorSelection(
                _lines, selection.AnchorLineIndex, selection.AnchorColumn,
                selection.HeadLineIndex, selection.HeadColumn);
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

    void IRenderHost<EditorLinesView>.Upsert(in EditorLinesView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var identities = ValidateLines(view);
        BeginUpdate();
        try { ApplyLines(view, identities); }
        catch { _rebuildLines = true; throw; }
        finally { EndUpdate(); }
    }

    private static HashSet<int> ValidateLines(in EditorLinesView view)
    {
        var identities = new HashSet<int>(view.Lines.Length);
        var previousIndex = -1;
        foreach (var line in view.Lines.Span) {
            if (line.Index <= previousIndex || !identities.Add(line.Identity)) {
                throw new ArgumentException("Editor lines must be ordered and have unique identities.", nameof(view));
            }
            previousIndex = line.Index;
        }
        return identities;
    }

    private void ApplyLines(in EditorLinesView view, HashSet<int> identities)
    {
        var rebuild = _rebuildLines;
        if (rebuild) ResetLines();
        if (_lineOwner.Apply(view, identities)) _selectionNeedsUpdate = true;
        if (rebuild) {
            _documentSize.Text(_statusView.DocumentSize);
            _cursorPosition.Text(_statusView.CursorPosition);
            SetDocumentRevision(_documentRevision);
            DomRuntime.SetDocumentLines(_lines, _totalLines);
        }
        _rebuildLines = false;
    }

    void IRenderHost<EditorLinesView>.Remove(in EditorLinesView view)
    {
        if (_disposed) return;
        BeginUpdate();
        try {
            ResetLines();
            _rebuildLines = false;
        }
        catch {
            _rebuildLines = true;
            throw;
        }
        finally {
            EndUpdate();
        }
    }

    private void ResetLines()
    {
        try {
            _lineOwner.Clear();
        }
        finally {
            _gutter.Text(string.Empty);
            _lines.Text(string.Empty);
            _gutter.Append(_gutterBefore).Append(_gutterAfter);
            _lines.Append(_linesBefore).Append(_linesAfter);
            SetHeight(_gutterBefore, _spacerBeforePx);
            SetHeight(_linesBefore, _spacerBeforePx);
            SetHeight(_gutterAfter, _spacerAfterPx);
            SetHeight(_linesAfter, _spacerAfterPx);
            _selectionNeedsUpdate = true;
        }
    }

    void IRenderHost<EditorActiveLineView>.Upsert(in EditorActiveLineView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeLineIdentity == view.Identity) {
            return;
        }
        _lineOwner.SetActiveLine(view.Identity);
        _activeLineIdentity = view.Identity;
    }

    void IRenderHost<EditorActiveLineView>.Remove(in EditorActiveLineView view)
    {
        if (_activeLineIdentity != view.Identity) {
            return;
        }
        _lineOwner.SetActiveLine(null);
        _activeLineIdentity = null;
    }

    void IRenderHost<EditorSelectionView>.Upsert(in EditorSelectionView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var unchanged = _selection == view;
        _selection = view;
        if (_suppressSelectionUpdate) {
            _suppressSelectionUpdate = false;
            return;
        }
        if (unchanged) {
            return;
        }
        _selectionNeedsUpdate = true;
    }

    void IRenderHost<EditorSelectionView>.Remove(in EditorSelectionView view)
    {
        _selection = null;
    }

    void IRenderHost<EditorStatusView>.Upsert(in EditorStatusView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _statusView = view;
        _documentSize.Text(view.DocumentSize);
        _cursorPosition.Text(view.CursorPosition);
    }

    void IRenderHost<EditorStatusView>.Remove(in EditorStatusView view) { }

    void IRenderHost<EditorDocumentView>.Upsert(in EditorDocumentView view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _totalLines = view.TotalLines;
        DomRuntime.SetDocumentLines(_lines, view.TotalLines);
    }

    void IRenderHost<EditorDocumentView>.Remove(in EditorDocumentView view) { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        void Release(Action action)
        {
            try { action(); } catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (_updateDepth > 0) {
            _updateDepth = 0;
            Release(() => DomRuntime.EndUpdate(_lines));
        }
            Release(() => DomRuntime.DetachGutterHeights(_lines));
            Release(() => DomRuntime.DetachEditorSurface(_cellId, _lines));
            Release(_lineOwner.Dispose);
            _selection = null;
            _activeLineIdentity = null;
            Release(() => _status.Remove());
            Release(() => _scroll.Remove());
            Release(_status.Dispose);
            Release(_cursorPosition.Dispose);
            Release(_documentSize.Dispose);
            Release(_gutterBefore.Dispose);
            Release(_gutterAfter.Dispose);
            Release(_linesBefore.Dispose);
            Release(_linesAfter.Dispose);
            Release(_lines.Dispose);
            Release(_gutter.Dispose);
            Release(_scroll.Dispose);
            Release(() => _container.ToggleClass("editor-container", false));
        if (failures is { Count: 1 }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is not null) throw new AggregateException(failures);
    }
}
