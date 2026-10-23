using Sia.Examples.Dom;
using Sia.Examples.Notebook;
using Sia;
using Sia.Reactive;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost : IDisposable
{
    private readonly string _cellId;
    private readonly World _world;
    private readonly CSharpCompletionProvider _completionProvider;
    private readonly BrowserEditorView _view;
    private readonly ReactiveMount<EditorViewProps> _mount;

    private readonly EditorViewState _viewState;

    private State<EditorState>? _state;
    private bool _disposed;

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
        _viewState = EditorViewState.Create(initialState.Doc, lineWrapping: true);
        _view.BeginUpdate();
        try {
            _view.SetDocumentRevision(0);
            _mount = _world.Mount(EditorViewComponent.Definition, new(_view, initialState));
        }
        finally {
            _view.EndUpdate();
        }
    }

    public string Source => State.Value.Doc.SliceDoc();

    internal event Action<BrowserEditorHost>? SourceChanged;

    internal void InvalidateLanguageContext() { CancelCompletion(); CancelSignature(); CancelLanguageCommand(); }

    internal (int Line, int Column, int Selected, int Lines, int Length) DocumentStatus {
        get {
            var state = State.Value;
            var head = state.Selection.Main.Head;
            var line = state.Doc.LineAt(head);
            return (line.Number, head - line.From + 1,
                state.Selection.Ranges.Sum(static range => range.To - range.From), state.Doc.Lines, state.Doc.Length);
        }
    }

    internal string BindingId => _cellId;

    internal void Focus() { if (_find.IsOpen) _find.Focus(); else _view.Focus(); }

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
                "stroke" => HandleStroke(arguments),
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

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _modalPending = default;
        _workbenchCommands.Clear();
        _bindings = [];
        _inputSession = null;
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
        _pendingRestore = null;
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
        else if (documentChanges is not null || !state.Selection.Eq(before.Selection, true)
            && !(_keymap.Profile == KeyboardProfile.Vim && InputMode == EditorInputMode.Insert)) {
            EndHistoryGroup();
        }

        ScheduleHighlight(state.Doc.SliceDoc());
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

    private State<EditorState> State
        => _state ??= _mount.GetState<EditorState>();

    private string CompletionScheduleKey => $"completion:{_cellId}";
}
