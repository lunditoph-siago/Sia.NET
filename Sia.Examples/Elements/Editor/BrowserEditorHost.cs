using System.Globalization;
using System.Text.Json;
using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed partial class BrowserEditorHost : IDisposable
{
    private const int _highlightDelayMilliseconds = 150;


    private readonly string _cellId;
    private readonly World _world;
    private readonly CSharpCompletionProvider _completionProvider;
    private readonly BrowserEditorView _view;
    private readonly ReactiveMount<EditorViewProps> _mount;

    private readonly EditorViewState _viewState;

    private State<EditorState>? _state;
    private State<EditorViewport>? _viewport;
    private double _lastClientHeight = 400;
    private double _lastScrollTop;
    private bool _disposed;
    private Text? _sourceDocument;
    private string _source = string.Empty;
    private string _highlightedSource;
    private int _highlightGeneration;
    private string? _highlightPendingSource;
    private CancellationTokenSource? _highlightCancellation;
    private Task<RangeSet<Decoration>>? _highlightWork;
    private int _lastNativeSequence;
    private int _documentRevision;
    private int _lastInputSequence;
    private int _scrollGeneration;
    private EditorMemento? _pendingRestore;
    private readonly Dictionary<int, DomDocument> _domDocuments = [];
    private readonly bool _nativeInput = DomRuntime.Backend.SupportsNativeInput;

    public BrowserEditorHost(
        World world,
        DomElement container,
        string cellId,
        string source,
        IReadOnlyList<HighlightRun> highlights,
        CSharpCompletionProvider completionProvider)
    {
        _cellId = $"{cellId}~{Guid.NewGuid():N}";
        _world = world;
        _view = new(_cellId, container);
        _find = new(container, _cellId);
        _completionProvider = completionProvider;
        _highlightedSource = source;
        var initialState = EditorState.Create(
            source,
            EditorDecorations.FromHighlights(highlights));
        _domDocuments.Add(0, new(initialState.Doc, ChangeSet.Empty(initialState.Doc.Length), 0));
        _viewState = EditorViewState.Create(initialState.Doc, lineWrapping: false);
        _view.BeginUpdate();
        try {
            _view.SetDocumentRevision(0);
            _mount = _world.Mount(EditorViewComponent.Definition, new(_view, initialState));
        }
        finally {
            _view.EndUpdate();
        }
    }

    public string Source => ReadSource(State.Value.Doc);

    internal event Action<BrowserEditorHost>? SourceChanged;

    internal void InvalidateLanguageContext() { CancelCompletion(); CancelSignature(); CancelLanguageCommand(); }

    internal (int Line, int Column, int Selected) CursorPosition {
        get {
            var state = State.Value;
            var head = state.Selection.Main.Head;
            var line = state.Doc.LineAt(head);
            return (line.Number, head - line.From + 1,
                state.Selection.Ranges.Sum(static range => range.To - range.From));
        }
    }

    private string ReadSource(Text document)
    {
        if (!ReferenceEquals(_sourceDocument, document)) {
            _source = document.SliceDoc();
            _sourceDocument = document;
        }
        return _source;
    }

    internal string BindingId => _cellId;

    internal void Focus() { if (_find.IsOpen) _find.Focus(); else _view.Focus(); }

    internal bool CanExecuteCommand(EditorCommand command) => command.Context switch {
        CommandContext.Editor => true,
        CommandContext.Suggestions => _completion is { HasItems: true } completion && (command.Id != EditorCommandId.AcceptSuggestion
            || completion.Items[_completionIndex].Edit is not null && ReferenceEquals(_completionDocument, State.Value.Doc)
            && State.Value.Selection.Main.Empty && State.Value.Selection.Main.Head == _completionPosition),
        CommandContext.Signatures => _signatureHelp is not null,
        _ => false,
    };

    internal bool ExecuteWorkbenchCommand(EditorCommand command)
    {
        SynchronizeNativeInput();
        return CanExecuteCommand(command) && ExecuteCommand(command, new(-1, -1, -1, -1), _completionGeneration);
    }

    public void SynchronizeNativeInput(bool sourceBoundary = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var input = _view.TakePendingInput(sourceBoundary);
        if (input.Length > 0) {
            _view.BeginUpdate();
            try {
                foreach (var pending in JsonSerializer.Deserialize<string[]>(input)!) {
                    HandleInput(pending);
                }
            }
            finally {
                _view.EndUpdate();
            }
        }
    }

    public bool Route(string eventType, string arguments)
    {
        if (_disposed) {
            return false;
        }
        if (eventType == "measure") {
            return HandleMeasure(arguments);
        }
        _view.BeginUpdate();
        try {
            return eventType switch {
                "input" => HandleQueuedInput(arguments),
                "find" => HandleFind(arguments),
                "clipboard" => HandleClipboard(arguments),
                "noop" => true,
                "key" => HandleKey(arguments),
                "command" => HandleCommand(arguments),
                "text" => HandleTextInput(arguments),
                "paste" => HandleTextInput(arguments, isolateHistory: true),
                "mut" => HandleDomMutation(arguments),
                "sel" => HandleSelection(arguments),
                "complete" => HandleCompletionRequest(arguments),
                "suggestion" => HandleCompletionClick(arguments),
                "suggestion-window" => HandleCompletionWindow(arguments),
                "signature" => HandleSignatureRequest(arguments),
                "signature-step" => HandleSignatureStep(arguments),
                "highlight" => HandleHighlightRequest(arguments),
                "highlight-ready" => HandleHighlightReady(arguments),
                _ => false,
            };
        }
        finally {
            _view.EndUpdate();
        }
    }

    private bool HandleQueuedInput(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out _) || sequence <= _lastInputSequence) {
            return HandleInput(arguments);
        }
        if (!HandleInput(arguments)) {
            return false;
        }
        var pending = _view.TakePendingInput(sourceBoundary: false);
        if (pending.Length > 0) {
            foreach (var input in JsonSerializer.Deserialize<string?[]>(pending)!) {
                if (input is null) {
                    break;
                }
                HandleInput(input);
            }
        }
        return true;
    }

    private bool HandleInput(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var input)) {
            return false;
        }
        var separator = input.IndexOf(':');
        if (separator < 0 || input[..separator] is not ("key" or "command" or "text" or "paste" or "mut" or "sel" or "clipboard" or "noop" or "suggestion")) {
            return false;
        }
        if (sequence <= _lastInputSequence) {
            return sequence > 0;
        }
        if (sequence != _lastInputSequence + 1) {
            return false;
        }
        _lastInputSequence = sequence;
        try {
            Route(input[..separator], input[(separator + 1)..]);
            return true;
        }
        finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleClipboard(string arguments)
    {
        if (!int.TryParse(arguments, out var requestId) || requestId <= 0) {
            return false;
        }
        var state = State.Value;
        var ranges = state.Selection.Ranges.Where(static range => !range.Empty).ToArray();
        var text = ranges.Length == 0
            ? null
            : string.Join('\n', ranges.Select(range => state.Doc.SliceDoc(range.From, range.To)));
        DomRuntime.ResolveEditorClipboard(_cellId, requestId, text);
        return true;
    }

    private bool HandleMeasure(string arguments)
    {
        var parts = arguments.Split(':', 9);
        if (parts.Length < 9
            || !TryParseInvariant(parts[0], out var scrollTop)
            || !TryParseInvariant(parts[1], out var clientHeight)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var firstLineIndex)
            || !TryParseInvariant(parts[3], out var contentWidth)
            || !TryParseInvariant(parts[4], out var charWidth)
            || !TryParseInvariant(parts[5], out var lineHeight)
            || !int.TryParse(parts[7], out var revision)
            || !int.TryParse(parts[8], out var scrollGeneration)
            || scrollGeneration > _scrollGeneration || scrollGeneration < -_scrollGeneration
            || revision < 0 || revision > _documentRevision) {
            return false;
        }

        foreach (var obsolete in _domDocuments.Keys.Where(value => value < revision).ToArray()) {
            _domDocuments.Remove(obsolete);
        }
        if (revision != _documentRevision) {
            return false;
        }

        if (scrollGeneration < 0) {
            _pendingRestore = null;
            scrollGeneration = -scrollGeneration;
        }

        double[]? heights = null;
        if (parts[6].Length > 0 && !TryParseHeights(parts[6], out heights)) {
            return false;
        }

        _lastClientHeight = clientHeight;
        var state = State.Value;
        var oracle = _viewState.SetDocument(state.Doc);

        var oracleChanged = false;
        var lineLength = contentWidth / charWidth;
        if (charWidth > 1 && lineHeight > 0 && contentWidth > 20 && lineLength >= 10) {
            oracleChanged = oracle.Refresh(
                lineWrapping: true,
                lineHeight: lineHeight,
                charWidth: charWidth,
                textHeight: lineHeight,
                lineLength: lineLength,
                knownHeights: heights ?? []);
        }

        if (oracleChanged && _pendingRestore is null
            && _view.ReadScrollPosition() is { Top: > 0, Line: >= 0 } position
            && position.Line < state.Doc.Lines) {
            _pendingRestore = new EditorMemento(state.Selection, position.Line, position.Column,
                position.Offset, position.Top, position.Left);
        }

        _view.BeginUpdate();
        try {
            var hasMeasuredLines = heights is { Length: > 0 } && firstLineIndex >= 0 && firstLineIndex < state.Doc.Lines;
            if (hasMeasuredLines || oracleChanged) {
                _viewState.UpdateMeasuredHeights(firstLineIndex, heights, force: oracleChanged);
            }

            if (oracleChanged && _pendingRestore is not null) {
                ++_scrollGeneration;
            }
            if (scrollGeneration != _scrollGeneration) {
                if (_pendingRestore is { } pending) {
                    RestoreViewport(pending);
                    _world.FlushReactive();
                }
                return true;
            }
            _pendingRestore = null;

            var bias = scrollTop - _lastScrollTop;
            _lastScrollTop = scrollTop;
            var viewport = _viewState.ComputeViewport(scrollTop, scrollTop + clientHeight, bias);
            if (_viewState.IsAppropriate(Viewport.Value, bias)) {
                viewport = Viewport.Value;
                _viewState.SetMainViewport(viewport);
            }
            ApplyViewport(viewport);
            _world.FlushReactive();
            return true;
        }
        finally {
            _view.EndUpdate();
        }
    }

    private static bool TryParseInvariant(string value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryParseHeights(string csv, out double[] heights)
    {
        var segments = csv.Split(',');
        heights = new double[segments.Length];
        for (var index = 0; index < segments.Length; index++) {
            if (!TryParseInvariant(segments[index], out heights[index])) {
                return false;
            }
        }
        return true;
    }

    private void ApplyViewport(EditorViewport viewport)
    {
        if (!viewport.Equals(Viewport.Value)) {
            Viewport.Set(viewport);
        }
        PushSpacerHeights(viewport);
    }

    private void PushSpacerHeights(EditorViewport viewport)
    {
        var map = _viewState.HeightMap;
        var before = viewport.From <= 0
            ? 0
            : _viewState.LineAt(viewport.From, QueryType.ByPos).Top;
        var afterBlock = _viewState.LineAt(viewport.To, QueryType.ByPos);
        var after = Math.Max(0, map.Height - afterBlock.Bottom);
        _view.SetSpacerHeights(before, after);
    }

    internal void UpdateHighlights(IReadOnlyList<HighlightRun> highlights)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelHighlight();
        _view.BeginUpdate();
        try {
            _highlightedSource = Source;
            Commit(State.Value.WithDecorations(EditorDecorations.FromHighlights(highlights)), revealSelection: false);
        }
        finally {
            _view.EndUpdate();
        }
    }

    public bool TryGetPosition(out EditorMemento memento)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var state = State.Value;
        var history = CaptureHistory();
        if (_pendingRestore is { } pending) {
            memento = pending with { Selection = state.Selection, History = history };
            return true;
        }
        var position = _view.ReadScrollPosition();
        var captured = position ?? new(_lastScrollTop, 0, -1, 0, 0);
        if (captured.Line < 0) {
            var block = _viewState.LineAt(captured.Top, QueryType.ByHeight);
            captured = captured with {
                Line = state.Doc.LineAt(block.From).Number - 1,
                Offset = (block.Top - captured.Top) / _viewState.Oracle.LineHeight,
            };
        }
        memento = new(state.Selection, captured.Line, captured.Column, captured.Offset, captured.Top, captured.Left) { History = history };
        return true;
    }

    public void RestorePosition(EditorMemento memento)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RestoreHistory(memento.History);
        _view.BeginUpdate();
        try {
            var state = State.Value;
            var selection = memento.Selection.Ranges.All(range => range.To <= state.Doc.Length)
                ? memento.Selection
                : EditorSelection.Create([.. memento.Selection.Ranges.Select(range => range.Clamp(state.Doc.Length))],
                    memento.Selection.MainIndex);
            ++_scrollGeneration;
            _pendingRestore = memento with { Selection = selection, History = null };
            Commit(state.Apply(new() { Selection = selection }), revealSelection: false);
            RestoreViewport(_pendingRestore.Value);
            _world.FlushReactive();
        }
        finally {
            _view.EndUpdate();
        }
    }

    private void RestoreViewport(EditorMemento memento)
    {
        var doc = State.Value.Doc;
        var line = doc.Line(Math.Clamp(memento.ViewLine + 1, 1, doc.Lines));
        var block = _viewState.LineAt(line.From, QueryType.ByPos);
        var column = Math.Clamp(memento.ViewColumn, 0, line.Length);
        var rowTop = Math.Min(block.Height - _viewState.Oracle.LineHeight,
            Math.Floor(column / _viewState.Oracle.LineLength) * _viewState.Oracle.LineHeight);
        var top = Math.Max(0, block.Top + Math.Max(0, rowTop) - memento.ViewOffset * _viewState.Oracle.LineHeight);
        ApplyViewport(_viewState.ComputeViewport(top, top + _lastClientHeight));
        _view.RestoreScroll(_scrollGeneration, new(top, memento.ScrollLeft, line.Number - 1, column, memento.ViewOffset));
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _find.Dispose();
        SourceChanged = null;
        DefinitionReady = null;
        ReferencesReady = null;
        RenameReady = null;
        CancelLanguageCommand();
        CancelCompletion();
        CancelSignature();
        CancelHighlight();
        if (_mount.IsMounted) {
            _mount.Unmount();
        }
        _view.Dispose();
        _domDocuments.Clear();
        ClearHistory();
        _sourceDocument = null;
        _source = string.Empty;
        _pendingRestore = null;
    }

    private bool HandleTextInput(string arguments, bool isolateHistory = false)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) {
            return false;
        }

        try {
            var before = State.Value;
            if (!TryReadTextCommand(
                commandArguments,
                out var nativeSelection,
                out var text)) {
                return false;
            }

            var editBefore = ApplyNativeSelection(before, nativeSelection);
            if (!isolateHistory && TryCommitCompletionCharacter(editBefore, text)) return true;
            var after = editBefore;
            TextCommands.Insert(new(after, state => after = state), text);
            Commit(after, historySelection: editBefore.Selection, historyGroup:
                !isolateHistory && text.Length > 0 && !text.Contains('\n') && !text.Contains('\r')
                    && editBefore.Selection.Ranges.All(static range => range.Empty) ? EditGroup.Typing : EditGroup.None);
            ScheduleCompletion(before, after);
            return true;
        }
        finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleDomMutation(string arguments)
    {
        if (!TryReadDomMutation(arguments, out var mutation)
            || mutation.Sequence <= _lastNativeSequence
            || !_domDocuments.TryGetValue(mutation.DocumentRevision, out var basis)
            || basis.NativeSequence != mutation.BaseSequence) {
            return false;
        }
        var before = State.Value;
        var nativeSelection = TryResolveSelection(basis.Document, mutation.Before, out var resolved)
            ? resolved
            : EditorSelection.Single(Math.Clamp(before.Selection.Main.Head, 0, basis.Document.Length));
        if (!TryCreateDomChange(basis.Document, nativeSelection, mutation, out var change)) {
            return false;
        }
        var nativeChanges = change is { } edit
            ? ChangeSet.Of([edit], basis.Document.Length)
            : ChangeSet.Empty(basis.Document.Length);
        var nativeDocument = nativeChanges.Apply(basis.Document);
        var transformed = ChangeSet.Transform(nativeChanges, basis.PendingChanges);
        var selection = TryResolveSelection(nativeDocument, mutation.After, out var nativeAfter)
            ? nativeAfter
            : nativeSelection.Map(nativeChanges);
        var modelChanges = transformed.Left;
        var pendingChanges = transformed.Right;
        var mappedSelection = selection.Map(pendingChanges, assoc: 1);
        if ((basis.PendingNavigation || mutation.SelectionSource == NativeSelectionSource.Model)
            && mutation.SelectionSource != NativeSelectionSource.Pointer) {
            var replacement = change;
            if (mutation.SelectionSource == NativeSelectionSource.Model && mutation.ReplacesSelection
                && !nativeSelection.Main.Empty) {
                var range = nativeSelection.Main;
                var replacementEnd = range.To + nativeDocument.Length - basis.Document.Length;
                if (replacementEnd >= range.From && replacementEnd <= nativeDocument.Length) {
                    replacement = new(range.From, range.To, nativeDocument.SliceDoc(range.From, replacementEnd));
                }
            }
            if (replacement is { } nativeEdit) {
                var nativeRange = nativeSelection.Main;
                var modelRange = before.Selection.Main;
                var from = mutation.SelectionSource == NativeSelectionSource.Model
                    ? modelRange.From
                    : Math.Clamp(
                        basis.PendingChanges.MapPos(nativeEdit.From, 1)!.Value
                            + modelRange.From - basis.PendingChanges.MapPos(nativeRange.From, 1)!.Value,
                        0, before.Doc.Length);
                var to = mutation.SelectionSource == NativeSelectionSource.Model
                    ? modelRange.To
                    : Math.Clamp(
                        basis.PendingChanges.MapPos(nativeEdit.To ?? nativeEdit.From, 1)!.Value
                            + modelRange.To - basis.PendingChanges.MapPos(nativeRange.To, 1)!.Value,
                        from, before.Doc.Length);
                var retargeted = ChangeSet.Of([new(from, to, nativeEdit.Insert ?? string.Empty)], before.Doc.Length);
                pendingChanges = ChangeSet.Compose(
                    ChangeSet.Compose(pendingChanges, modelChanges.Invert(before.Doc)), retargeted);
                modelChanges = retargeted;
                var insertedLength = nativeEdit.Insert?.Length ?? 0;
                int MapPoint(int position, int association)
                    => position >= nativeEdit.From && position <= nativeEdit.From + insertedLength
                        ? from + position - nativeEdit.From
                        : pendingChanges.MapPos(position, association)!.Value;
                mappedSelection = mutation.SelectionSource == NativeSelectionSource.Model && selection.Main.Empty
                    ? EditorSelection.Single(from + insertedLength)
                    : EditorSelection.Create([.. selection.Ranges.Select(range =>
                        EditorSelection.Range(MapPoint(range.Anchor, 1), MapPoint(range.Head, range.Empty ? 1 : -1)))],
                        selection.MainIndex);
            }
            else {
                mappedSelection = before.Selection;
            }
        }
        var edits = modelChanges.ToSpecs();
        var historySelection = (basis.PendingNavigation || mutation.SelectionSource == NativeSelectionSource.Model)
            && mutation.SelectionSource != NativeSelectionSource.Pointer
            ? before.Selection : nativeSelection.Map(basis.PendingChanges);
        var after = before.Apply(new() { Changes = edits, Selection = mappedSelection });
        _view.PrepareReconciliation(mutation.Sequence);
        _lastNativeSequence = mutation.Sequence;
        CancelCompletion();
        _view.RestoreDocumentStructure();
        Commit(after, nativeCommit: true, historySelection: historySelection,
            historyGroup: historySelection.Main.Empty
                ? NativeHistoryGroup(mutation.InputType, nativeSelection, change) : EditGroup.None);
        _domDocuments[mutation.DocumentRevision] = new(
            nativeDocument, pendingChanges, mutation.Sequence, basis.PendingNavigation);
        ScheduleCompletion(before, after);
        return true;
    }

    private static bool TryCreateDomChange(
        Text document,
        EditorSelection beforeSelection,
        DomMutation mutation,
        out ChangeSpec? change)
    {
        if (mutation.LineIndex < 0 || mutation.WindowEnd <= mutation.LineIndex
            || mutation.WindowEnd > document.Lines) {
            change = null;
            return false;
        }
        var windowStart = document.Line(mutation.LineIndex + 1);
        var windowEnd = document.Line(mutation.WindowEnd);
        var windowLength = windowEnd.To - windowStart.From;
        var from = Math.Clamp(beforeSelection.Main.From - windowStart.From, 0, windowLength);
        var to = Math.Clamp(beforeSelection.Main.To - windowStart.From, from, windowLength);
        var window = document.Slice(windowStart.From, windowEnd.To);
        var difference = TextDiff.FindForSelection(
            window.Layout.SliceDoc(), Text.OfString(mutation.Replacement).Layout.SliceDoc(), window.ToLayoutPosition(from), window.ToLayoutPosition(to),
            mutation.InputType.Contains("Backward", StringComparison.Ordinal)
                ? TextDiff.Preference.End : TextDiff.Preference.None);
        change = difference is { } edit
            ? new(windowStart.From + window.FromLayoutPosition(edit.From), windowStart.From + window.FromLayoutPosition(edit.To),
                edit.Insert.Replace("\n", document.NewlineAt(windowStart.From + window.FromLayoutPosition(edit.From)), StringComparison.Ordinal))
            : null;
        return true;
    }

    private static bool TryReadDomMutation(string arguments, out DomMutation mutation)
    {
        var prefix = arguments.Split(':', 7);
        if (prefix.Length != 7 || prefix[0] != "c"
            || !int.TryParse(prefix[1], out var sequence) || sequence <= 0
            || !int.TryParse(prefix[2], out var revision) || revision < 0
            || !int.TryParse(prefix[3], out var baseSequence) || baseSequence < 0 || baseSequence >= sequence
            || prefix[4] is not ("0" or "1" or "2")
            || prefix[5] is not ("0" or "1")) {
            mutation = default;
            return false;
        }
        var parts = prefix[6].Split(':', 12);
        if (parts.Length != 12
            || !int.TryParse(parts[0], out var lineIndex)
            || !int.TryParse(parts[1], out var windowEnd)
            || !TryReadSelection(parts, 2, out var before)
            || !TryReadSelection(parts, 6, out var after)) {
            mutation = default;
            return false;
        }
        mutation = new(lineIndex, windowEnd, before, after,
            Uri.UnescapeDataString(parts[10]), Uri.UnescapeDataString(parts[11]),
            sequence, revision, baseSequence, prefix[4] switch {
                "1" => NativeSelectionSource.Pointer,
                "2" => NativeSelectionSource.Model,
                _ => NativeSelectionSource.Dom,
            }, prefix[5] == "1");
        return true;
    }

    private static bool TryReadTextCommand(
        string arguments,
        out BrowserSelection selection,
        out string text)
    {
        var parts = arguments.Split(':', 5);
        selection = default;
        if (parts.Length < 5 || !TryReadSelection(parts, 0, out selection)) {
            text = string.Empty;
            return false;
        }
        text = Uri.UnescapeDataString(parts[4]);
        return true;
    }

    private static bool TryReadSelection(
        string[] parts,
        int offset,
        out BrowserSelection selection)
    {
        if (parts.Length < offset + 4
            || !int.TryParse(parts[offset], out var anchorLineIndex)
            || !int.TryParse(parts[offset + 1], out var anchorColumn)
            || !int.TryParse(parts[offset + 2], out var headLineIndex)
            || !int.TryParse(parts[offset + 3], out var headColumn)) {
            selection = default;
            return false;
        }
        selection = new(
            anchorLineIndex,
            anchorColumn,
            headLineIndex,
            headColumn);
        return true;
    }

    private static bool TryResolveSelection(
        Text document,
        BrowserSelection native,
        out EditorSelection selection)
    {
        if (native.AnchorLineIndex < 0
            || native.AnchorLineIndex >= document.Lines
            || native.HeadLineIndex < 0
            || native.HeadLineIndex >= document.Lines
            || native.AnchorColumn < 0
            || native.HeadColumn < 0) {
            selection = null!;
            return false;
        }

        var anchorLine = document.Line(native.AnchorLineIndex + 1);
        var headLine = document.Line(native.HeadLineIndex + 1);
        selection = EditorSelection.Single(
            Math.Clamp(
                anchorLine.From + native.AnchorColumn,
                anchorLine.From,
                anchorLine.To),
            Math.Clamp(
                headLine.From + native.HeadColumn,
                headLine.From,
                headLine.To));
        return true;
    }

    private static EditorState ApplyNativeSelection(
        EditorState state,
        BrowserSelection native)
    {
        if (!TryResolveSelection(state.Doc, native, out var selection)
            || selection.Main.Anchor == state.Selection.Main.Anchor
                && selection.Main.Head == state.Selection.Main.Head) {
            return state;
        }
        return state.Apply(new() { Selection = selection });
    }

    private bool HandleSelection(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length is not (4 or 6) || !TryReadSelection(parts, 0, out var native)) {
            return false;
        }

        var state = State.Value;
        var document = state.Doc;
        ChangeSet? pendingChanges = null;
        if (parts.Length == 6) {
            if (!int.TryParse(parts[4], out var revision)
                || !int.TryParse(parts[5], out var nativeSequence)
                || !_domDocuments.TryGetValue(revision, out var basis)
                || basis.NativeSequence != nativeSequence) {
                return false;
            }
            document = basis.Document;
            pendingChanges = basis.PendingChanges;
        }
        if (!TryResolveSelection(document, native, out var selection)) {
            return false;
        }
        if (pendingChanges is not null) {
            selection = selection.Map(pendingChanges, assoc: 1);
        }
        foreach (var revision in _domDocuments.Keys.ToArray()) {
            _domDocuments[revision] = _domDocuments[revision] with { PendingNavigation = false };
        }
        _view.CancelSelectionReveal();
        if (_view.ReadScrollPosition() is { } position) {
            _lastScrollTop = position.Top;
            ApplyViewport(_viewState.ComputeViewport(position.Top, position.Top + _lastClientHeight));
        }
        if (selection.Eq(state.Selection, true)) {
            _world.FlushReactive();
            return false;
        }

        if (pendingChanges is null || pendingChanges.IsEmpty) {
            _view.SuppressNextSelectionUpdate();
        }
        Commit(state.Apply(new() { Selection = selection }), revealSelection: false);
        CancelCompletion();
        return true;
    }

    private bool HandleKey(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) {
            return false;
        }

        try {
            return HandleKeyCommand(commandArguments);
        }
        finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleKeyCommand(string arguments)
    {
        var parts = arguments.Split(':', 8);
        if (parts.Length < 8
            || !TryReadSelection(parts, 0, out var nativeSelection)
            || !bool.TryParse(parts[5], out var control)
            || !bool.TryParse(parts[6], out var shift)
            || !bool.TryParse(parts[7], out var alt)) {
            return false;
        }
        var modifiers = (control ? ShortcutModifiers.Primary : ShortcutModifiers.None)
            | (alt ? ShortcutModifiers.Alt : ShortcutModifiers.None) | (shift ? ShortcutModifiers.Shift : ShortcutModifiers.None);
        var key = parts[4] == " " ? "Space" : parts[4].Length == 1 ? parts[4].ToUpperInvariant() : parts[4];
        var context = _completion is { HasItems: true } ? CommandContext.Suggestions
            : _signatureHelp is not null ? CommandContext.Signatures : CommandContext.Editor;
        var command = EditorKeymap.Default.Resolve(new(key, modifiers), context);
        var fallback = context == CommandContext.Suggestions ? EditorKeymap.Default.Resolve(new(key, modifiers), CommandContext.Editor) : null;
        return command?.IsEditor == true && ExecuteCommand(command, nativeSelection, _completionGeneration, fallback);
    }

    private bool HandleCommand(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) return false;
        try {
            var parts = commandArguments.Split(':', 7);
            if (parts.Length < 5 || !TryReadSelection(parts, 0, out var selection)
                || EditorCommand.Find(parts[4]) is not { IsEditor: true } command) return false;
            var fallback = parts.Length > 5 ? EditorCommand.Find(parts[5]) : null;
            var generation = parts.Length > 6 && int.TryParse(parts[6], out var value) ? value : -1;
            return ExecuteCommand(command, selection, generation, fallback);
        }
        finally { DomRuntime.AcknowledgeEditorCommand(_cellId, sequence); }
    }

    private bool ExecuteCommand(EditorCommand command, BrowserSelection nativeSelection, int generation,
        EditorCommand? fallback = null)
    {
        if (command.Context == CommandContext.Suggestions) {
            if (generation != _completionGeneration || _completion is not { HasItems: true })
                return fallback is { IsEditor: true, Context: not CommandContext.Suggestions }
                    && ExecuteCommand(fallback, nativeSelection, generation);
            switch (command.Id) {
                case EditorCommandId.AcceptSuggestion:
                    if (_completion.Items[_completionIndex].Edit is null || !ReferenceEquals(_completionDocument, State.Value.Doc)
                        || !State.Value.Selection.Main.Empty || State.Value.Selection.Main.Head != _completionPosition || fallback?.Id == EditorCommandId.Newline
                        && (_completionSoft || !CompletionMakesEdit(_completion.Items[_completionIndex]))) {
                        CancelCompletion();
                        return fallback is { IsEditor: true } && ExecuteCommand(fallback, nativeSelection, generation);
                    }
                    AcceptCompletion(_completion); break;
                case EditorCommandId.NextSuggestion: SelectCompletion((_completionIndex + 1) % _completion.Items.Count); break;
                case EditorCommandId.PreviousSuggestion: SelectCompletion((_completionIndex - 1 + _completion.Items.Count) % _completion.Items.Count); break;
                case EditorCommandId.NextSuggestionPage: SelectCompletion(Math.Min(_completionIndex + _completionPageSize, _completion.Items.Count - 1)); break;
                case EditorCommandId.PreviousSuggestionPage: SelectCompletion(Math.Max(0, _completionIndex - _completionPageSize)); break;
                case EditorCommandId.HideSuggestions: CancelCompletion(); break;
            }
            return true;
        }
        if (command.Id is EditorCommandId.Undo or EditorCommandId.Redo)
            return ReplayHistory(command.Id == EditorCommandId.Redo);
        if (command.Id is EditorCommandId.Find or EditorCommandId.Replace or EditorCommandId.NextMatch or EditorCommandId.PreviousMatch)
            return ExecuteFindCommand(command.Id, nativeSelection);
        if (command.Id == EditorCommandId.Escape && _find.IsOpen) { _find.Close(); _view.SetFindMatch(false); return true; }
        if (command.Id is EditorCommandId.GoToDefinition or EditorCommandId.FindReferences or EditorCommandId.RenameSymbol or EditorCommandId.FormatDocument) return StartLanguageCommand(command, nativeSelection);
        if (command.Id == EditorCommandId.Suggest) return HandleManualCompletion(nativeSelection);
        if (command.Id == EditorCommandId.SignatureHelp) {
            var state = ApplyNativeSelection(State.Value, nativeSelection);
            Commit(state);
            _view.RevealSelection();
            ScheduleSignature(state, manual: true);
            return true;
        }
        if (command.Context == CommandContext.Signatures) {
            if (command.Id == EditorCommandId.HideSignature) CancelSignature();
            else StepSignature(command.Id == EditorCommandId.NextSignature ? 1 : -1);
            return true;
        }
        if (command.Id == EditorCommandId.Escape && _signatureRequested && _completion is null) {
            CancelCompletion();
            CancelSignature();
            return true;
        }
        if (command.Edit is not { } edit) return false;
        if ((_completionPending || _completion is not null) && command.Id is
            EditorCommandId.Indent or EditorCommandId.Outdent or EditorCommandId.Newline or EditorCommandId.Escape
            or EditorCommandId.Up or EditorCommandId.Down or EditorCommandId.SelectUp or EditorCommandId.SelectDown) {
            CancelCompletion();
            if (command.Id == EditorCommandId.Escape) return true;
        }
        var before = State.Value;
        var editBefore = ApplyNativeSelection(before, nativeSelection);
        var after = editBefore;
        if (!edit(new(after, state => after = state))) {
            var viewport = EnsureSelectionVisible(after, Viewport.Value);
            _view.RevealSelection();
            ApplyViewport(viewport);
            _world.FlushReactive();
            return false;
        }
        Commit(after, keyboardNavigation: ReferenceEquals(after.Doc, before.Doc)
            && !after.Selection.Eq(before.Selection, true), historySelection: editBefore.Selection,
            historyGroup: editBefore.Selection.Ranges.All(static range => range.Empty)
                ? command.Id switch { EditorCommandId.Backspace => EditGroup.Backward, EditorCommandId.Delete => EditGroup.Forward, _ => EditGroup.None }
                : EditGroup.None);
        ScheduleCompletion(before, after);
        return true;
    }

    private static bool TryReadCommand(
        string arguments,
        out int sequence,
        out string commandArguments)
    {
        var separator = arguments.IndexOf(':');
        sequence = 0;
        if (separator < 0 || !int.TryParse(arguments[..separator], out sequence)) {
            commandArguments = string.Empty;
            return false;
        }

        commandArguments = arguments[(separator + 1)..];
        return true;
    }

    private void Commit(
        EditorState state, bool nativeCommit = false, bool keyboardNavigation = false, bool revealSelection = true,
        EditorSelection? historySelection = null, EditGroup historyGroup = EditGroup.None, bool recordHistory = true)
    {
        if (state == State.Value && !nativeCommit) {
            return;
        }
        var before = State.Value;
        ChangeSet? documentChanges = null;
        if (!ReferenceEquals(state.Doc, before.Doc) || nativeCommit || !state.Selection.Eq(before.Selection, true)) {
            var changes = ReferenceEquals(state.Doc, before.Doc)
                ? ChangeSet.Empty(before.Doc.Length)
                : state.LineUpdate is { } documentUpdate && ReferenceEquals(documentUpdate.BeforeDocument, before.Doc)
                    ? documentUpdate.Changes
                    : throw new InvalidOperationException("Editor changes must describe the current document.");
            if (!ReferenceEquals(state.Doc, before.Doc)) documentChanges = changes;
            if (_nativeInput) {
                foreach (var revision in _domDocuments.Keys.ToArray()) {
                    var basis = _domDocuments[revision];
                    _domDocuments[revision] = basis with {
                        PendingChanges = ChangeSet.Compose(basis.PendingChanges, changes),
                        PendingNavigation = basis.PendingNavigation || keyboardNavigation,
                    };
                }
            }
            else {
                _domDocuments.Clear();
            }
            _domDocuments.Add(++_documentRevision, new(state.Doc, ChangeSet.Empty(state.Doc.Length), 0));
            _view.SetDocumentRevision(_documentRevision);
        }
        State.Set(state);
        if (documentChanges is not null) { _find.Refresh(state.Doc); _view.SetFindMatch(false); }
        if (documentChanges is not null || _languageCommand != EditorCommandId.FormatDocument && !state.Selection.Eq(before.Selection, true)) CancelLanguageCommand();
        if (documentChanges is not null) SourceChanged?.Invoke(this);
        if (documentChanges is { } edit && recordHistory) {
            RecordHistory(edit, before, state, historySelection ?? before.Selection, historyGroup);
        }
        else if (documentChanges is not null || !state.Selection.Eq(before.Selection, true)) {
            EndHistoryGroup();
        }

        ScheduleHighlight(ReadSource(state.Doc));
        var viewport = Viewport.Value;
        if (!ReferenceEquals(state.LineUpdate, before.LineUpdate) && state.LineUpdate is { } update) {
            _viewState.ApplyChanges(update, state.Doc);

            var mapped = _viewState.MapViewport(viewport, update.Changes).Clamp(state.Doc.Length);
            viewport = _viewState.IsAppropriate(mapped)
                ? mapped
                : _viewState.ComputeViewport(_lastScrollTop, _lastScrollTop + _lastClientHeight);
            _viewState.SetMainViewport(viewport);
        }

        if (revealSelection) {
            _pendingRestore = null;
            viewport = EnsureSelectionVisible(state, viewport);
            _view.RevealSelection();
        }
        ApplyViewport(viewport);
        _world.FlushReactive();
        if (documentChanges is not null || !state.Selection.Eq(before.Selection, true)) ScheduleSignature(state);
    }

    private EditorViewport EnsureSelectionVisible(EditorState state, EditorViewport viewport)
    {
        var headPosition = state.Selection.Main.Head;
        if (!viewport.Contains(headPosition)) {
            viewport = _viewState.EnsureIncludes(
                new EditorScrollTarget(headPosition, ScrollYStrategy.Nearest), _lastClientHeight);
        }

        return viewport;
    }

    private void ScheduleHighlight(string source)
    {
        if (source == _highlightPendingSource) {
            return;
        }
        if (source == _highlightedSource) {
            if (_highlightPendingSource is not null) {
                CancelHighlight();
            }
            return;
        }
        CancelHighlight();
        _highlightPendingSource = source;
        DomRuntime.ScheduleEvent(
            HighlightScheduleKey,
            $"highlight:{_cellId}:{_highlightGeneration}",
            _highlightDelayMilliseconds);
    }

    private bool HandleHighlightRequest(string arguments)
    {
        if (!int.TryParse(arguments, out var generation) || generation != _highlightGeneration
            || _highlightPendingSource is not { } source) {
            return false;
        }
        if (_highlightWork is not null) {
            return true;
        }
        var cancellation = new CancellationTokenSource();
        _highlightCancellation = cancellation;
        var token = cancellation.Token;
        var work = CSharpCompilationEngine.RunRoslynAsync(() => {
            var highlights = CSharpHighlighter.Classify(source, token);
            token.ThrowIfCancellationRequested();
            var decorations = EditorDecorations.FromHighlights(highlights);
            token.ThrowIfCancellationRequested();
            return decorations;
        }, token);
        _highlightWork = work;
        _ = PublishHighlightAsync(work, token, _cellId, generation);
        return true;
    }

    private static async Task PublishHighlightAsync(
        Task<RangeSet<Decoration>> work, CancellationToken cancellation, string binding, int generation)
    {
        try { await work; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch { }
        if (cancellation.IsCancellationRequested) {
            return;
        }
        try {
            DomRuntime.ScheduleEvent($"highlight:{binding}", $"highlight-ready:{binding}:{generation}", 0);
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
    }

    private bool HandleHighlightReady(string arguments)
    {
        if (!int.TryParse(arguments, out var generation) || generation != _highlightGeneration
            || _highlightWork is not { IsCompleted: true } work || _highlightPendingSource is not { } source) {
            return false;
        }
        _highlightPendingSource = null;
        _highlightWork = null;
        _highlightCancellation?.Dispose();
        _highlightCancellation = null;
        var decorations = work.GetAwaiter().GetResult();
        if (source != Source) {
            return false;
        }
        State.Set(State.Value.WithDecorations(decorations));
        _world.FlushReactive();
        _highlightedSource = source;
        return true;
    }

    private void CancelHighlight()
    {
        ++_highlightGeneration;
        DomRuntime.CancelScheduledEvent(HighlightScheduleKey);
        _highlightPendingSource = null;
        var cancellation = _highlightCancellation;
        var work = _highlightWork;
        _highlightCancellation = null;
        _highlightWork = null;
        if (cancellation is not null) {
            cancellation.Cancel();
            _ = ReleaseHighlightAsync(work!, cancellation);
        }
    }

    private static async Task ReleaseHighlightAsync(Task work, CancellationTokenSource cancellation)
    {
        try { await work.ConfigureAwait(false); }
        catch { }
        finally { cancellation.Dispose(); }
    }

    private enum NativeSelectionSource
    {
        Dom,
        Pointer,
        Model,
    }

    private readonly record struct BrowserSelection(
        int AnchorLineIndex,
        int AnchorColumn,
        int HeadLineIndex,
        int HeadColumn);

    private readonly record struct DomMutation(
        int LineIndex,
        int WindowEnd,
        BrowserSelection Before,
        BrowserSelection After,
        string InputType,
        string Replacement,
        int Sequence,
        int DocumentRevision,
        int BaseSequence,
        NativeSelectionSource SelectionSource,
        bool ReplacesSelection);

    private readonly record struct DomDocument(
        Text Document, ChangeSet PendingChanges, int NativeSequence, bool PendingNavigation = false);

    private State<EditorState> State
        => _state ??= _mount.GetState<EditorState>();

    private State<EditorViewport> Viewport
        => _viewport ??= _mount.GetState<EditorViewport>(1);

    private string CompletionScheduleKey => $"completion:{_cellId}";

    private string HighlightScheduleKey => $"highlight:{_cellId}";
}
