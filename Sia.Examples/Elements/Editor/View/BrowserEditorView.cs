using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

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
    private readonly DomElement _inputMode;
    private readonly DomElement _documentSize;
    private readonly DomElement _cursorPosition;
    private readonly List<DomElement> _touchActions = [];
    private readonly IEditorLinesOwner _lineOwner;

    private DomElement? _completionAction;
    private DomElement _modeAction = null!;
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
    private (KeyboardProfile Profile, EditorInputMode Mode)? _projectedInputMode;

    public BrowserEditorView(string cellId, DomElement container)
    {
        _cellId = cellId;
        _container = container;
        _container.Class("editor-container");

        _gutter = DomElement.Create("div").Class("editor-gutter");
        _lines = DomElement.Create("div")
            .Class("editor-lines")
            .Attr("contenteditable", "true")
            .Attr("role", "textbox")
            .Attr("aria-label", "C# editor")
            .Attr("aria-multiline", "true")
            .Attr("aria-autocomplete", "list")
            .Attr("spellcheck", "false")
            .Attr("writingsuggestions", "false")
            .Attr("autocorrect", "off")
            .Attr("autocapitalize", "off");
        _scroll = DomElement.Create("div").Class("editor-scroll");
        _status = DomElement.Create("div").Class("editor-status");
        _inputMode = DomElement.Create("span").Class("editor-input-mode").Class("hidden").Attr("aria-live", "polite");
        _documentSize = DomElement.Create("span").Class("editor-document-size");
        _cursorPosition = DomElement.Create("span").Class("editor-cursor-position");

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
        _status.Append(CreateTouchActions()).Append(_inputMode).Append(_documentSize).Append(_cursorPosition);
        _container.Append(_scroll).Append(_status);
        DomRuntime.AttachEditorSurface(_cellId, _lines);
        DomRuntime.AttachGutterHeights(_gutter, _lines, _cellId);
    }

    internal void SetLineWrapping(bool enabled) => _lines.ToggleClass("editor-nowrap", !enabled);

    private DomElement CreateTouchActions()
    {
        DomElement Own(DomElement element) { _touchActions.Add(element); return element; }
        var actions = Own(DomElement.Create("div").Class("editor-touch-actions")
            .Attr("role", "group").Attr("aria-label", "Editor actions"));
        foreach (var (command, label, path) in new[] {
            (EditorCommandId.Escape, "Escape", ""),
            (EditorCommandId.Indent, "Indent", "M10 5h10M10 10h10M10 15h10M4 5l4 5-4 5M4 20h16"),
            (EditorCommandId.Outdent, "Outdent", "M10 5h10M10 10h10M10 15h10M8 5l-4 5 4 5M4 20h16"),
            (EditorCommandId.Undo, "Undo", "m8 4-5 5 5 5M3 9h11a6 6 0 0 1 0 12"),
            (EditorCommandId.Redo, "Redo", "m16 4 5 5-5 5M21 9H10a6 6 0 0 0 0 12"),
            (EditorCommandId.Suggest, "Show completions", "M4 5h10M4 10h7M4 15h7M4 20h10m2-12 5 5-5 5"),
        }) {
            var button = Own(DomElement.Create("button").Attr("type", "button")
                .Attr("data-editor-touch-command", command.ToString()).Attr("aria-label", label).Attr("title", label));
            if (command == EditorCommandId.Escape) {
                _modeAction = button;
                actions.Append(button.Text("Esc"));
                continue;
            }
            if (command == EditorCommandId.Suggest) _completionAction = button.Attr("aria-expanded", "false");
            var icon = Own(DomElement.Create("svg").Attr("viewBox", "0 0 24 24")
                .Attr("width", "18").Attr("height", "18").Attr("fill", "none")
                .Attr("stroke", "currentColor").Attr("stroke-width", "1.5")
                .Attr("stroke-linecap", "round").Attr("stroke-linejoin", "round")
                .Attr("aria-hidden", "true").Attr("focusable", "false"));
            actions.Append(button.Append(icon.Append(Own(DomElement.Create("path").Attr("d", path)))));
        }
        return actions;
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

    internal void SetInputMode(KeyboardProfile profile, EditorInputMode mode)
    {
        if (_projectedInputMode == (profile, mode)) return;
        _projectedInputMode = (profile, mode);
        _lines.Attr("data-editor-profile", profile.ToString()).Attr("data-editor-mode", mode.ToString());
        _inputMode.Text(mode.ToString().ToUpperInvariant()).ToggleClass("hidden", profile != KeyboardProfile.Vim);
        var insert = profile == KeyboardProfile.Vim && mode == EditorInputMode.Normal;
        _modeAction.Attr("data-editor-touch-command", (insert ? EditorCommandId.InsertMode : EditorCommandId.Escape).ToString())
            .Attr("aria-label", insert ? "Enter Insert mode" : "Escape")
            .Attr("title", insert ? "Enter Insert mode" : "Escape").Text(insert ? "Insert" : "Esc");
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

    internal void SetFindMatch(bool enabled)
    {
        _lines.Attr("data-editor-find-match", enabled ? "true" : "false");
        _selectionNeedsUpdate = true;
    }

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

    private static bool AlmostEqual(double a, double b) => System.Math.Abs(a - b) < 0.5;

    private static void SetHeight(DomElement element, double px)
        => element.Attr("style", $"height:{System.Math.Max(0, px).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px");

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

    public void MountOverlay(DomElement overlay, int lineIndex, int column, int endLineIndex = -1, int endColumn = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _container.Append(overlay);
        PlaceOverlay(overlay, lineIndex, column, endLineIndex, endColumn);
    }

    internal void SetCompletionSelection(string listId, string itemId, int generation = -1)
    {
        _lines.Attr("aria-controls", listId).Attr("aria-activedescendant", itemId);
        var expanded = listId.Length > 0;
        var label = expanded ? "Hide completions" : "Show completions";
        _completionAction?.Attr("aria-expanded", expanded ? "true" : "false")
            .Attr("aria-label", label).Attr("title", label)
            .Attr("data-editor-touch-command", (expanded ? EditorCommandId.HideSuggestions : EditorCommandId.Suggest).ToString())
            .Attr("data-editor-touch-generation", generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal void Focus() => _lines.Focus();

    internal void SetSignatureHelp(string id) => _lines.Attr("aria-describedby", id);

    public void PlaceOverlay(DomElement overlay, int lineIndex, int column, int endLineIndex = -1, int endColumn = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DomRuntime.PlaceOverlay(_container, _lines, overlay, lineIndex, column, endLineIndex, endColumn);
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
        Release(_inputMode.Dispose);
        Release(_cursorPosition.Dispose);
        Release(_documentSize.Dispose);
        foreach (var element in _touchActions) Release(element.Dispose);
        _touchActions.Clear();
        _completionAction = null;
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
