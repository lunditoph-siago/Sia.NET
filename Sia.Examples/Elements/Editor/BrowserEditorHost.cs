#if BROWSER

using Sia;
using Sia.Reactive;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorHost : IDisposable
{
    private readonly string _cellId;
    private readonly BrowserEditorView _view;
    private readonly IEditorCompletionProvider _completionProvider;
    private readonly EditorState _initialState;

    private bool _built;
    private World _world = null!;
    private World? _prevContext;
    private ReactiveMount<EditorViewProps> _mount;
    private State<EditorState> _stateCell;

    private CompletionQueryResult? _completion;
    private int _completionIndex;
    private int _completionGeneration;
    private CancellationTokenSource? _debounceCts;
    private const int DebounceMs = 80;

    private static readonly Dictionary<string, BrowserEditorHost> _hosts = [];

    private BrowserEditorHost(BrowserElement container, string cellId, string initialSource,
        IReadOnlyList<HighlightRun> highlights, IMetadataReferenceProvider references)
    {
        _cellId = cellId;
        _completionProvider = new RoslynCompletionProvider(references);
        _view = new BrowserEditorView(cellId, container.Handle);

        var state = EditorState.Create(new EditorStateConfig {
            Doc = Text.OfString(initialSource),
            Selection = EditorSelection.Single(0),
            Extensions = [EditorHighlights.Field],
        });
        _initialState = highlights.Count == 0 ? state : state.Apply(new TransactionSpec {
            Effects = [EditorHighlights.SetHighlights.Of(ToDecorations(highlights))],
        });
    }

    private static RangeSet<Decoration> ToDecorations(IReadOnlyList<HighlightRun> runs)
        => EditorHighlights.FromRuns(runs.Select(r => (r.Start, r.Length, r.Classification)));

    public void Build()
    {
        if (_built) return;
        _built = true;

        _prevContext = Context<World>.Current;
        _world = new World();
        Context<World>.Current = _world;

        _view.Build();
        var props = new EditorViewProps(_view, _initialState, StandardKeymap.Bindings, false, 4);
        _mount = _world.Mount(EditorViewComponent.Definition, props);
        _stateCell = _mount.GetState<EditorState>(0);
    }


    public static bool RouteEvent(string payload)
    {
        var idx = payload.IndexOf(':');
        if (idx < 0) return false;
        var type = payload[..idx];
        var rest = payload[(idx + 1)..];

        var idx2 = rest.IndexOf(':');
        if (idx2 < 0) return false;
        var cellId = rest[..idx2];
        var args = rest[(idx2 + 1)..];

        var host = _hosts.GetValueOrDefault(cellId);
        if (host is not { _built: true }) return false;

        return type switch {
            "key" => host.OnKey(args),
            "mutline" => host.OnMutLine(args),
            "mutall" => host.OnMutAll(args),
            "sel" => host.OnSelect(args),
            _ => false,
        };
    }


    private bool OnMutLine(string args)
    {
        var nul = args.IndexOf('\0');
        if (nul < 0 || !int.TryParse(args[..nul], out var lineIndex)) return false;
        var newText = args[(nul + 1)..];

        var before = _stateCell.Value;
        if (lineIndex < 0 || lineIndex >= before.Doc.Lines) return false;
        var line = before.Doc.Line(lineIndex + 1);
        var diff = TextDiff.Minimal(line.Text, newText);
        if (diff is null) return true;
        var (from, to, insert) = diff.Value;

        var spec = new ChangeSpec(line.From + from, line.From + to, insert);
        var after = before.Apply(new TransactionSpec {
            Changes = [spec],
            Selection = EditorSelection.Single(line.From + from + insert.Length),
            UserEvent = "input.type",
        });
        _view.PrepareChanges(SingleChange(spec, before.Doc.Length), before.Doc, after.Doc);
        _view.SuppressNextSelectionSync();
        _view.NoteNativeLineEdit(lineIndex);
        _stateCell.Set(after);
        _world.FlushReactive();
        MaybeScheduleCompletion(before, after);
        return true;
    }

    private bool OnMutAll(string args)
    {
        var nul = args.IndexOf('\0');
        var newText = nul < 0 ? args : args[(nul + 1)..];

        var before = _stateCell.Value;
        var oldText = before.Doc.SliceDoc();
        var diff = TextDiff.Minimal(oldText, newText);
        if (diff is null) return true;
        var (from, to, insert) = diff.Value;

        var spec = new ChangeSpec(from, to, insert);
        var after = before.Apply(new TransactionSpec {
            Changes = [spec],
            Selection = EditorSelection.Single(from + insert.Length),
            UserEvent = "input",
        });
        _view.PrepareChanges(SingleChange(spec, before.Doc.Length), before.Doc, after.Doc);
        _view.SuppressNextSelectionSync();
        _stateCell.Set(after);
        _world.FlushReactive();
        MaybeScheduleCompletion(before, after);
        return true;
    }

    private bool OnSelect(string args)
    {
        var parts = args.Split(':');
        if (parts.Length < 4 ||
            !int.TryParse(parts[0], out var aLn) || !int.TryParse(parts[1], out var aCol) ||
            !int.TryParse(parts[2], out var hLn) || !int.TryParse(parts[3], out var hCol)) return false;

        var state = _stateCell.Value;
        if (aLn < 0 || aLn >= state.Doc.Lines || hLn < 0 || hLn >= state.Doc.Lines) return false;
        var anchor = Math.Clamp(state.Doc.Line(aLn + 1).From + aCol, 0, state.Doc.Length);
        var head = Math.Clamp(state.Doc.Line(hLn + 1).From + hCol, 0, state.Doc.Length);
        var newSel = EditorSelection.Single(anchor, head);
        if (newSel.Eq(state.Selection, true)) return false;

        var newState = state.Apply(new TransactionSpec { Selection = newSel });
        _view.SuppressNextSelectionSync();
        _stateCell.Set(newState);
        _world.FlushReactive();
        ClosePopupIfOpen();
        return true;
    }


    private bool OnKey(string args)
    {
        var parts = args.Split(':');
        if (parts.Length < 4) return false;
        var key = parts[0];
        var ctrl = bool.Parse(parts[1]);
        var shift = bool.Parse(parts[2]);

        if (_completion is not null && key is "ArrowUp" or "ArrowDown" or "Enter" or "Tab" or "Escape") {
            HandleCompletionKey(key);
            return true;
        }

        var cmd = MapKey(key, ctrl, shift);
        if (cmd == null) return false;

        var before = _stateCell.Value;
        var after = before;
        var handled = cmd(new CommandTarget(before, s => after = s));
        if (!handled) return false;

        if (DeriveChanges(before, after) is { } changes) _view.PrepareChanges(changes, before.Doc, after.Doc);

        _stateCell.Set(after);
        _world.FlushReactive();
        MaybeScheduleCompletion(before, after);
        return true;
    }


    private void HandleCompletionKey(string key)
    {
        if (_completion is not { } open) return;
        switch (key) {
            case "ArrowDown":
                _completionIndex = (_completionIndex + 1) % open.Items.Count;
                RenderCompletionPopup();
                break;
            case "ArrowUp":
                _completionIndex = (_completionIndex - 1 + open.Items.Count) % open.Items.Count;
                RenderCompletionPopup();
                break;
            case "Enter":
            case "Tab":
                AcceptCompletion(open);
                break;
            case "Escape":
                ClosePopupIfOpen();
                break;
        }
    }

    private void MaybeScheduleCompletion(EditorState before, EditorState after)
    {
        if (!after.Selection.Main.Empty) { ClosePopupIfOpen(); return; }
        var pos = after.Selection.Main.Head;
        bool shouldQuery;
        if (after.Doc.Length > before.Doc.Length && pos > 0 && pos <= after.Doc.Length) {
            var ch = after.SliceDoc(pos - 1, pos)[0];
            shouldQuery = ch is '.' or '_' || char.IsLetterOrDigit(ch);
        } else {
            shouldQuery = after.Doc.Length < before.Doc.Length;
        }

        if (shouldQuery) ScheduleCompletion(after);
        else ClosePopupIfOpen();
    }

    private void ScheduleCompletion(EditorState state)
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedQueryAsync(state, cts);
    }

    private async Task DebouncedQueryAsync(EditorState state, CancellationTokenSource cts)
    {
        try { await Task.Delay(DebounceMs, cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        finally { if (_debounceCts == cts) _debounceCts = null; }

        var gen = ++_completionGeneration;
        CompletionQueryResult result;
        try {
            result = await _completionProvider.QueryAsync(
                state.Doc.SliceDoc(), state.Selection.Main.Head, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"Completion failed: {ex}");
            if (gen == _completionGeneration) ClosePopupIfOpen();
            return;
        }
        if (gen != _completionGeneration || cts.IsCancellationRequested) return;
        _completionIndex = 0;
        _completion = result.IsOpen ? result : null;
        if (result.IsOpen) RenderCompletionPopup();
        else ClosePopupIfOpen();
    }

    private void AcceptCompletion(CompletionQueryResult completion)
    {
        var item = completion.Items[_completionIndex];
        ClosePopupIfOpen();
        var state = _stateCell.Value;
        var spec = new ChangeSpec(item.ReplaceStart, item.ReplaceEnd, item.InsertText);
        var newState = state.Apply(new TransactionSpec {
            Changes = [spec],
            Selection = EditorSelection.Single(item.ReplaceStart + item.InsertText.Length),
            ScrollIntoView = true,
            UserEvent = "input.complete",
        });
        _view.PrepareChanges(SingleChange(spec, state.Doc.Length), state.Doc, newState.Doc);
        _stateCell.Set(newState);
        _world.FlushReactive();
    }


    private static ChangeSet SingleChange(ChangeSpec spec, int docLength)
        => ChangeSet.Of([spec], docLength);

    private static ChangeSet? DeriveChanges(EditorState before, EditorState after)
    {
        if (before.Doc.Equals(after.Doc)) return null;
        var diff = TextDiff.Minimal(before.Doc.SliceDoc(), after.Doc.SliceDoc());
        if (diff is null) return null;
        var (from, to, insert) = diff.Value;
        return SingleChange(new ChangeSpec(from, to, insert), before.Doc.Length);
    }

    private void RenderCompletionPopup()
    {
        if (_completion is not { } q) return;
        HideCompletionPopup();

        var container = BrowserDomBridge.Find(NotebookRenderer.EditorContainerId(_cellId));
        var popup = BrowserDomBridge.Create("div");
        BrowserDomBridge.ToggleClass(popup, "completion-popup", true);
        BrowserDomBridge.SetId(popup, CompletionPopupId);
        for (var i = 0; i < q.Items.Count; i++) {
            var item = BrowserDomBridge.Create("div");
            BrowserDomBridge.ToggleClass(item, "completion-item", true);
            BrowserDomBridge.ToggleClass(item, "selected", i == _completionIndex);
            BrowserDomBridge.SetText(item, q.Items[i].Label);
            BrowserDomBridge.InsertBefore(popup, item, null);
        }
        BrowserDomBridge.InsertBefore(container, popup, null);
    }

    private void HideCompletionPopup()
    {
        var popup = BrowserDomBridge.Find(CompletionPopupId);
        if (popup is not null) BrowserDomBridge.Remove(popup);
    }

    private string CompletionPopupId => $"cmpl-{_cellId}";

    private void ClosePopupIfOpen()
    {
        _debounceCts?.Cancel();
        if (_completion is null) return;
        _completion = null;
        HideCompletionPopup();
    }


    private static StateCommand? MapKey(string key, bool ctrl, bool shift)
    {
        return (key, ctrl, shift) switch {
            ("ArrowLeft", false, _) => CursorCommands.CharLeft,
            ("ArrowRight", false, _) => CursorCommands.CharRight,
            ("ArrowUp", false, _) => CursorCommands.LineUp,
            ("ArrowDown", false, _) => CursorCommands.LineDown,
            ("Home", false, _) => CursorCommands.LineStart,
            ("End", false, _) => CursorCommands.LineEnd,
            ("Home", true, _) => CursorCommands.DocStart,
            ("End", true, _) => CursorCommands.DocEnd,
            ("PageUp", false, _) => t => CursorCommands.PageUp(t, 20),
            ("PageDown", false, _) => t => CursorCommands.PageDown(t, 20),
            ("ArrowLeft", true, _) => CursorCommands.GroupLeft,
            ("ArrowRight", true, _) => CursorCommands.GroupRight,

            ("Enter", false, _) => LineCommands.InsertNewlineAndIndent,
            ("Backspace", false, _) => DeleteCommands.CharBackward,
            ("Delete", false, _) => DeleteCommands.CharForward,
            ("Tab", false, false) => LineCommands.InsertTab,
            ("Tab", false, true) => LineCommands.IndentLess,

            ("a", true, _) => SelectionCommands.SelectAll,
            ("k", true, _) => DeleteCommands.ToLineEnd,
            ("Escape", false, _) => SelectionCommands.SimplifySelection,

            _ => null,
        };
    }


    public static BrowserEditorHost GetOrCreate(
        BrowserElement container, string cellId, string initialSource,
        IReadOnlyList<HighlightRun> highlights, IMetadataReferenceProvider references)
    {
        if (_hosts.TryGetValue(cellId, out var existing)) { existing.Dispose(); _hosts.Remove(cellId); }
        var host = new BrowserEditorHost(container, cellId, initialSource, highlights, references);
        _hosts[cellId] = host;
        return host;
    }

    public static void BuildAll()
    { foreach (var h in _hosts.Values) h.Build(); }

    public static void DisposeAll()
    { foreach (var h in _hosts.Values.ToArray()) h.Dispose(); _hosts.Clear(); }

    public static string? ReadSource(string cellId)
        => _hosts.TryGetValue(cellId, out var h) && h._built ? h._stateCell.Value.Doc.SliceDoc() : null;

    public void Dispose()
    {
        _debounceCts?.Cancel();
        if (_built) {
            _mount.Unmount();
            _world.Dispose();
            Context<World>.Current = _prevContext;
        }
        _view.Dispose();
        (_completionProvider as IDisposable)?.Dispose();
    }
}
#endif
