using System.Globalization;
using System.Text.Json;
using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed partial class BrowserEditorHost : IDisposable
{
    private const int _completionDelayMilliseconds = 80;
    private const int _highlightDelayMilliseconds = 150;
    private const int _maximumRenderedCompletions = 20;

    private readonly string _cellId;
    private readonly World _world;
    private readonly BrowserEditorView _view;
    private readonly CSharpCompletionProvider _completionProvider;
    private readonly ReactiveMount<EditorViewProps> _mount;
    private readonly List<(DomElement Element, string Label)> _completionItems = [];

    private readonly EditorViewState _viewState;

    private State<EditorState>? _state;
    private State<EditorViewport>? _viewport;
    private double _lastClientHeight = 400;
    private double _lastScrollTop;
    private CompletionResult? _completion;
    private CompletionResult? _completionSource;
    private DomElement? _completionPopup;
    private CancellationTokenSource? _completionCancellation;
    private int _completionIndex;
    private int _completionGeneration;
    private int _completionQueryAnchor = -1;
    private bool _completionPending;
    private bool _completionQueryRunning;
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
        ICompilationReferenceResolver references)
    {
        _cellId = $"{cellId}~{Guid.NewGuid():N}";
        _world = world;
        _view = new(_cellId, container);
        _completionProvider = new(references);
        _highlightedSource = source;
        var initialState = EditorState.Create(
            source,
            EditorDecorations.FromHighlights(highlights));
        _domDocuments.Add(0, new(initialState.Doc, ChangeSet.Empty(initialState.Doc.Length), 0));
        var oracle = new EditorHeightOracle(lineWrapping: false).SetDoc(initialState.Doc);
        var heightMap = EditorHeightMap.Empty()
            .ApplyChanges(Text.Empty, oracle, [(0, 0, 0, initialState.Doc.Length)]);
        _viewState = new EditorViewState(heightMap, oracle);
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

    private string ReadSource(Text document)
    {
        if (!ReferenceEquals(_sourceDocument, document)) {
            _source = document.SliceDoc();
            _sourceDocument = document;
        }
        return _source;
    }

    internal string BindingId => _cellId;

    public void SynchronizeNativeInput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var input = _view.TakePendingInput();
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
                "clipboard" => HandleClipboard(arguments),
                "noop" => true,
                "key" => HandleKey(arguments),
                "text" => HandleTextInput(arguments),
                "paste" => HandleTextInput(arguments, isolateHistory: true),
                "mut" => HandleDomMutation(arguments),
                "sel" => HandleSelection(arguments),
                "complete" => HandleCompletionRequest(arguments),
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
        if (separator < 0 || input[..separator] is not ("key" or "text" or "paste" or "mut" or "sel" or "clipboard" or "noop")) {
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
        var oracle = _viewState.Oracle.SetDoc(state.Doc);

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
                EditorMeasuredHeights? measured = null;
                if (hasMeasuredLines) {
                    var from = state.Doc.Line(firstLineIndex + 1).From;
                    measured = new EditorMeasuredHeights(from, heights!);
                }
                _viewState.SetHeightMap(_viewState.HeightMap.UpdateHeight(oracle, 0, force: oracleChanged, measured));
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
        var oracle = _viewState.Oracle;
        var map = _viewState.HeightMap;
        var before = viewport.From <= 0
            ? 0
            : map.LineAt(viewport.From, QueryType.ByPos, oracle, 0, 0).Top;
        var afterBlock = map.LineAt(Math.Min(viewport.To, map.Length), QueryType.ByPos, oracle, 0, 0);
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
            var block = _viewState.HeightMap.LineAt(captured.Top, QueryType.ByHeight, _viewState.Oracle, 0, 0);
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
        var block = _viewState.HeightMap.LineAt(line.From, QueryType.ByPos, _viewState.Oracle, 0, 0);
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
        CancelCompletion();
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
        var difference = TextDiff.FindForSelection(
            document.SliceDoc(windowStart.From, windowEnd.To), mutation.Replacement, from, to,
            mutation.InputType.Contains("Backward", StringComparison.Ordinal)
                ? TextDiff.Preference.End : TextDiff.Preference.None);
        change = difference is { } edit
            ? new(windowStart.From + edit.From, windowStart.From + edit.To, edit.Insert)
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
        var key = parts[4];
        if (control && !alt && key is "z" or "y") {
            return ReplayHistory(key == "y" || shift);
        }
        if (key == "Tab" && shift && (_completionPending || _completion is not null)) {
            CancelCompletion();
        }

        if (_completion is not null
            && key is "ArrowUp" or "ArrowDown" or "Enter" or "Tab" or "Escape") {
            HandleCompletionKey(key);
            return true;
        }

        if (_completionPending
            && key is "ArrowUp" or "ArrowDown" or "Enter" or "Tab" or "Escape") {
            if (key == "Tab"
                && !control
                && !shift
                && HandleManualCompletion(nativeSelection)) {
                return true;
            }
            CancelCompletion();
            if (key == "Escape") {
                return true;
            }
        }

        if (key == "Tab"
            && !control
            && !shift
            && HandleManualCompletion(nativeSelection)) {
            return true;
        }

        var command = GetCommand(key, control, shift);
        if (command is null) {
            return false;
        }

        var before = State.Value;
        var editBefore = ApplyNativeSelection(before, nativeSelection);
        var after = editBefore;
        if (!command(new(after, state => after = state))) {
            var viewport = EnsureSelectionVisible(after, Viewport.Value);
            ApplyViewport(viewport);
            _world.FlushReactive();
            return false;
        }
        Commit(after, keyboardNavigation: ReferenceEquals(after.Doc, before.Doc)
            && !after.Selection.Eq(before.Selection, true), historySelection: editBefore.Selection,
            historyGroup: !control && editBefore.Selection.Ranges.All(static range => range.Empty)
                ? key switch { "Backspace" => EditGroup.Backward, "Delete" => EditGroup.Forward, _ => EditGroup.None }
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
        if (documentChanges is { } edit && recordHistory) {
            RecordHistory(edit, before, state, historySelection ?? before.Selection, historyGroup);
        } else if (documentChanges is not null || !state.Selection.Eq(before.Selection, true)) {
            EndHistoryGroup();
        }

        ScheduleHighlight(ReadSource(state.Doc));
        var viewport = Viewport.Value;
        if (!ReferenceEquals(state.LineUpdate, before.LineUpdate) && state.LineUpdate is { } update) {
            var ranges = new List<(int FromA, int ToA, int FromB, int ToB)>();
            update.Changes.IterateChangedRanges(
                (oldFrom, oldTo, newFrom, newTo) => ranges.Add((oldFrom, oldTo, newFrom, newTo)));
            var oracle = _viewState.Oracle.SetDoc(state.Doc);
            _viewState.SetHeightMap(_viewState.HeightMap.ApplyChanges(update.BeforeDocument, oracle, ranges));

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

    private void ScheduleCompletion(EditorState before, EditorState after)
    {
        var isActive = _completionPending || _completionSource is not null;
        if (!CompletionTrigger.ShouldActivate(before, after, isActive)) {
            CancelCompletion();
            return;
        }

        StartCompletion(after, _completionDelayMilliseconds);
    }

    private bool HandleManualCompletion(BrowserSelection nativeSelection)
    {
        var before = State.Value;
        var state = ApplyNativeSelection(before, nativeSelection);
        if (!state.Selection.Main.Empty) {
            return false;
        }
        if (state != before) {
            _view.SuppressNextSelectionUpdate();
            Commit(state);
        }
        StartCompletion(state, delayMilliseconds: 0);
        return true;
    }

    private void StartCompletion(EditorState state, int delayMilliseconds)
    {
        var position = state.Selection.Main.Head;
        var queryAnchor = CompletionIdentifier.FindStart(state.Doc, position);
        if (_completionPending && queryAnchor == _completionQueryAnchor) {
            if (!_completionQueryRunning) {
                DomRuntime.ScheduleEvent(
                    CompletionScheduleKey,
                    $"complete:{_cellId}:{_completionGeneration}",
                    delayMilliseconds);
            }
            return;
        }

        CancelCompletionQuery();
        if (_completionSource is { } source
            && source.TryFilter(
                state.Doc,
                position,
                _maximumRenderedCompletions,
                out var filtered)) {
            _completionPending = false;
            _completionIndex = 0;
            _completion = filtered;
            if (filtered.HasItems) {
                RenderCompletionPopup();
            }
            else {
                HideCompletionPopup();
            }
            return;
        }

        _completionPending = true;
        _completion = null;
        _completionSource = null;
        _completionQueryAnchor = queryAnchor;
        var cancellation = new CancellationTokenSource();
        _completionCancellation = cancellation;
        var generation = _completionGeneration;
        DomRuntime.ScheduleEvent(
            CompletionScheduleKey,
            $"complete:{_cellId}:{generation}",
            delayMilliseconds);
    }

    private bool HandleCompletionRequest(string arguments)
    {
        if (!int.TryParse(arguments, out var generation)
            || generation != _completionGeneration
            || _completionCancellation is not { } cancellation
            || cancellation.IsCancellationRequested) {
            return false;
        }
        if (_completionQueryRunning) {
            return true;
        }

        _completionQueryRunning = true;
        _ = QueryCompletionAsync(State.Value, cancellation, generation);
        return true;
    }

    private async Task QueryCompletionAsync(
        EditorState state,
        CancellationTokenSource cancellation,
        int generation)
    {
        try {
            cancellation.Token.ThrowIfCancellationRequested();

            var result = await _completionProvider.QueryAsync(
                state.Doc.SliceDoc(),
                state.Selection.Main.Head,
                cancellation.Token);
            if (generation != _completionGeneration || cancellation.IsCancellationRequested) {
                return;
            }

            var current = State.Value;
            var currentPosition = current.Selection.Main.Head;
            if (CompletionIdentifier.FindStart(current.Doc, currentPosition)
                != _completionQueryAnchor) {
                CancelCompletion();
                return;
            }

            _completionPending = false;
            _completionIndex = 0;
            if (!result.HasItems) {
                CloseCompletionPopup();
                return;
            }

            _completionSource = result;
            if (!result.TryFilter(
                current.Doc,
                currentPosition,
                _maximumRenderedCompletions,
                out var filtered)) {
                CloseCompletionPopup();
                return;
            }
            _completion = filtered;
            RenderCompletionPopup();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
        }
        catch (Exception error) {
            if (generation == _completionGeneration) {
                _completionPending = false;
                CloseCompletionPopup();
                DomRuntime.ReportError(error.ToString());
            }
        }
        finally {
            if (ReferenceEquals(_completionCancellation, cancellation)) {
                _completionCancellation = null;
                _completionQueryRunning = false;
            }
            cancellation.Dispose();
        }
    }

    private void HandleCompletionKey(string key)
    {
        if (_completion is not { } completion) {
            return;
        }
        switch (key) {
            case "ArrowDown":
                SelectCompletion((_completionIndex + 1) % completion.Items.Count);
                break;
            case "ArrowUp":
                SelectCompletion(
                    (_completionIndex - 1 + completion.Items.Count) % completion.Items.Count);
                break;
            case "Enter":
            case "Tab":
                AcceptCompletion(completion);
                break;
            case "Escape":
                CancelCompletion();
                break;
        }
    }

    private void AcceptCompletion(CompletionResult completion)
    {
        var candidate = completion.Items[_completionIndex];
        CancelCompletion();
        var state = State.Value;
        Commit(state.Apply(new() {
            Changes = [new(
                candidate.ReplaceStart,
                candidate.ReplaceEnd,
                candidate.InsertText)],
            Selection = EditorSelection.Single(
                candidate.ReplaceStart + candidate.InsertText.Length),
        }));
    }

    private void RenderCompletionPopup()
    {
        if (_completion is not { } completion) {
            CloseCompletionPopup();
            return;
        }
        if (!completion.HasItems) {
            HideCompletionPopup();
            return;
        }

        var isNew = _completionPopup is null;
        var popup = _completionPopup ??= DomElement.Create("div").Class("completion-popup");
        for (var index = 0; index < completion.Items.Count; index++) {
            var label = completion.Items[index].Label;
            DomElement item;
            if (index < _completionItems.Count) {
                var rendered = _completionItems[index];
                item = rendered.Element;
                if (rendered.Label != label) {
                    item.Text(label);
                    _completionItems[index] = (item, label);
                }
            }
            else {
                item = DomElement.Create("div").Class("completion-item").Text(label);
                popup.Append(item);
                _completionItems.Add((item, label));
            }
            item.ToggleClass("selected", index == _completionIndex);
        }
        while (_completionItems.Count > completion.Items.Count) {
            var item = _completionItems[^1].Element;
            item.Remove();
            item.Dispose();
            _completionItems.RemoveAt(_completionItems.Count - 1);
        }
        var anchorPosition = completion.Items.Min(static item => item.ReplaceStart);
        var anchorLine = State.Value.Doc.LineAt(anchorPosition);
        var anchorLineIndex = anchorLine.Number - 1;
        var anchorColumn = anchorPosition - anchorLine.From;
        if (isNew) {
            _view.MountOverlay(popup, anchorLineIndex, anchorColumn);
        }
        else {
            _view.PlaceOverlay(popup, anchorLineIndex, anchorColumn);
        }
        DomRuntime.EnsureVisible(popup, _completionItems[_completionIndex].Element);
    }

    private void SelectCompletion(int index)
    {
        _completionItems[_completionIndex].Element.ToggleClass("selected", false);
        _completionIndex = index;
        var selected = _completionItems[_completionIndex].Element;
        selected.ToggleClass("selected", true);
        if (_completionPopup is { } popup) {
            DomRuntime.EnsureVisible(popup, selected);
        }
    }

    private void ClearCompletionItems()
    {
        foreach (var (item, _) in _completionItems) {
            item.Remove();
            item.Dispose();
        }
        _completionItems.Clear();
    }

    private void CancelCompletion()
    {
        CancelCompletionQuery();
        _completionPending = false;
        CloseCompletionPopup();
    }

    private void CancelCompletionQuery()
    {
        _completionGeneration++;
        DomRuntime.CancelScheduledEvent(CompletionScheduleKey);
        var cancellation = _completionCancellation;
        var queryWasRunning = _completionQueryRunning;
        _completionCancellation = null;
        _completionQueryAnchor = -1;
        _completionQueryRunning = false;
        cancellation?.Cancel();
        if (!queryWasRunning) {
            cancellation?.Dispose();
        }
    }

    private void CloseCompletionPopup()
    {
        _completionSource = null;
        HideCompletionPopup();
    }

    private void HideCompletionPopup()
    {
        ClearCompletionItems();
        if (_completionPopup is { } popup) {
            DomRuntime.ClearOverlayPlacement(popup);
        }
        _completionPopup?.Remove();
        _completionPopup?.Dispose();
        _completionPopup = null;
        _completion = null;
    }

    private static StateCommand? GetCommand(string key, bool control, bool shift)
        => (key, control, shift) switch {
            ("ArrowLeft", false, false) => CursorCommands.CharLeft,
            ("ArrowLeft", false, true) => CursorCommands.SelectCharLeft,
            ("ArrowRight", false, false) => CursorCommands.CharRight,
            ("ArrowRight", false, true) => CursorCommands.SelectCharRight,
            ("ArrowUp", false, false) => CursorCommands.LineUp,
            ("ArrowUp", false, true) => CursorCommands.SelectLineUp,
            ("ArrowDown", false, false) => CursorCommands.LineDown,
            ("ArrowDown", false, true) => CursorCommands.SelectLineDown,
            ("Home", false, false) => CursorCommands.LineStart,
            ("Home", false, true) => CursorCommands.SelectLineStart,
            ("End", false, false) => CursorCommands.LineEnd,
            ("End", false, true) => CursorCommands.SelectLineEnd,
            ("Home", true, false) => CursorCommands.DocumentStart,
            ("Home", true, true) => CursorCommands.SelectDocumentStart,
            ("End", true, false) => CursorCommands.DocumentEnd,
            ("End", true, true) => CursorCommands.SelectDocumentEnd,
            ("PageUp", false, false) => CursorCommands.PageUp,
            ("PageUp", false, true) => CursorCommands.SelectPageUp,
            ("PageDown", false, false) => CursorCommands.PageDown,
            ("PageDown", false, true) => CursorCommands.SelectPageDown,
            ("ArrowLeft", true, false) => CursorCommands.GroupLeft,
            ("ArrowLeft", true, true) => CursorCommands.SelectGroupLeft,
            ("ArrowRight", true, false) => CursorCommands.GroupRight,
            ("ArrowRight", true, true) => CursorCommands.SelectGroupRight,
            ("Enter", false, _) => LineCommands.InsertNewlineAndIndent,
            ("Backspace", false, _) => DeleteCommands.CharBackward,
            ("Backspace", true, _) => DeleteCommands.GroupBackward,
            ("Delete", false, _) => DeleteCommands.CharForward,
            ("Delete", true, _) => DeleteCommands.GroupForward,
            ("Tab", false, false) => LineCommands.InsertTab,
            ("Tab", false, true) => LineCommands.IndentLess,
            ("a", true, _) => SelectionCommands.SelectAll,
            ("k", true, _) => DeleteCommands.ToLineEnd,
            ("Escape", false, _) => SelectionCommands.SimplifySelection,
            _ => null,
        };

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
