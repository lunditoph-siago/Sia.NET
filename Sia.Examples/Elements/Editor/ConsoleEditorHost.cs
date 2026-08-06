#if !BROWSER
using Sia;
using Sia.Reactive;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class ConsoleEditorHost : IDisposable
{
    private readonly ConsoleScreen _screen;
    private readonly SplitPaneRenderer _pane;
    private readonly ConsoleTermRenderer _view;
    private readonly World _world;
    private readonly World? _prevContext;
    private readonly ReactiveMount<EditorViewProps> _mount;
    private readonly State<EditorState> _stateCell;
    private readonly IEditorCompletionProvider _completionProvider;

    private CompletionQueryResult? _completion;
    private int _completionIndex;
    private (int Top, int Left, int Width, int Height)? _popupRect;

    public ConsoleEditorHost(
        ConsoleScreen screen, string cellId, string initialSource,
        IReadOnlyList<HighlightRun> highlights, IMetadataReferenceProvider references)
    {
        _screen = screen;
        _pane = new SplitPaneRenderer(screen);
        _completionProvider = new RoslynCompletionProvider(references);

        var layout = _pane.Layout();
        _view = new ConsoleTermRenderer(screen,
            layout.WorkspaceCol, 0,
            layout.WorkspaceWidth, layout.ContentHeight,
            gutterWidth: 5);

        _world = new World();
        _prevContext = Context<World>.Current;
        Context<World>.Current = _world;

        var state = EditorState.Create(new EditorStateConfig
        {
            Doc = Text.OfString(initialSource),
            Selection = EditorSelection.Single(0),
            Extensions = [EditorHighlights.Field],
        });
        if (highlights.Count > 0) {
            state = state.Apply(new TransactionSpec {
                Effects = [EditorHighlights.SetHighlights.Of(ToDecorations(highlights))],
            });
        }

        var props = new EditorViewProps(_view, state, StandardKeymap.Bindings, false, 4);
        _mount = _world.Mount(EditorViewComponent.Definition, props);
        _stateCell = _mount.GetState<EditorState>(0);
    }

    private static RangeSet<Decoration> ToDecorations(IReadOnlyList<HighlightRun> runs)
        => EditorHighlights.FromRuns(runs.Select(r => (r.Start, r.Length, r.Classification)));

    public string? Edit(IReadOnlyList<string> sidebarLines, ref int sidebarScroll)
    {
        Redraw(sidebarLines);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.S)
            {
                return _stateCell.Value.Doc.SliceDoc();
            }

            if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.Q)
            {
                return null;
            }

            if (_completion is { } open)
            {
                if (key.Key == ConsoleKey.Escape)
                {
                    _completion = null;
                    ClearPopupArea();
                }
                else if (HandleCompletionKey(key, open))
                {
                    continue;
                }
            }

            var cmd = MapKeyToCommand(key);
            if (cmd == null) continue;

            var state = _stateCell.Value;
            var handled = cmd(new CommandTarget(state, newState =>
            {
                _stateCell.Set(newState);
            }));

            if (handled)
            {
                UpdateCompletion(key, state, _stateCell.Value);
                _world.FlushReactive();
                DrawCompletionPopup();
            }
        }
    }

    private bool HandleCompletionKey(ConsoleKeyInfo key, CompletionQueryResult open)
    {
        switch (key.Key)
        {
            case ConsoleKey.DownArrow:
                _completionIndex = (_completionIndex + 1) % open.Items.Count;
                DrawCompletionPopup();
                return true;
            case ConsoleKey.UpArrow:
                _completionIndex = (_completionIndex - 1 + open.Items.Count) % open.Items.Count;
                DrawCompletionPopup();
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Tab:
                AcceptCompletion(open);
                _world.FlushReactive();
                DrawCompletionPopup();
                return true;
            default:
                return false;
        }
    }

    private void UpdateCompletion(ConsoleKeyInfo key, EditorState before, EditorState after)
    {
        var shouldQuery = !after.Selection.Main.Empty
            ? false
            : key.Key == ConsoleKey.Backspace
                || (key.Modifiers == 0 && key.KeyChar >= ' '
                    && (char.IsLetterOrDigit(key.KeyChar) || key.KeyChar is '_' or '.'));

        if (!shouldQuery)
        {
            _completion = null;
            ClearPopupArea();
            return;
        }

        if (Console.KeyAvailable)
        {
            return;
        }

        var source = after.Doc.SliceDoc();
        var position = after.Selection.Main.Head;
        var result = _completionProvider.QueryAsync(source, position).GetAwaiter().GetResult();

        if (!result.IsOpen) ClearPopupArea();
        _completion = result.IsOpen ? result : null;
        _completionIndex = 0;
    }

    private void AcceptCompletion(CompletionQueryResult completion)
    {
        var item = completion.Items[_completionIndex];
        _completion = null;
        ClearPopupArea();

        var state = _stateCell.Value;
        var newState = state.Apply(new TransactionSpec
        {
            Changes = [new ChangeSpec(item.ReplaceStart, item.ReplaceEnd, item.InsertText)],
            Selection = EditorSelection.Single(item.ReplaceStart + item.InsertText.Length),
            ScrollIntoView = true,
            UserEvent = "input.complete",
        });
        _stateCell.Set(newState);
    }

    private void DrawCompletionPopup()
    {
        if (_completion is not { } q) return;

        var lines = new List<string>(q.Items.Count);
        for (var i = 0; i < q.Items.Count; i++)
        {
            var marker = i == _completionIndex ? "▶ " : "  ";
            lines.Add(marker + q.Items[i].Label);
        }
        var width = Math.Clamp(lines.Count == 0 ? 10 : lines.Max(l => l.Length) + 1, 10, 40);
        var rect = _pane.RenderFloatingAt(_view.CursorScreenRow, _view.CursorScreenCol, lines, width);

        if (_popupRect is { } old && old != rect) _pane.ClearArea(old.Top, old.Left, old.Width, old.Height);
        _popupRect = rect;
    }

    private void ClearPopupArea()
    {
        if (_popupRect is not { } r) return;
        _pane.ClearArea(r.Top, r.Left, r.Width, r.Height);
        _popupRect = null;
    }

    private static StateCommand? MapKeyToCommand(ConsoleKeyInfo key)
    {
        return (key.Modifiers, key.Key) switch
        {
            (0, ConsoleKey.LeftArrow) => CursorCommands.CharLeft,
            (0, ConsoleKey.RightArrow) => CursorCommands.CharRight,
            (0, ConsoleKey.UpArrow) => CursorCommands.LineUp,
            (0, ConsoleKey.DownArrow) => CursorCommands.LineDown,
            (0, ConsoleKey.Home) => CursorCommands.LineStart,
            (0, ConsoleKey.End) => CursorCommands.LineEnd,
            (ConsoleModifiers.Control, ConsoleKey.LeftArrow) => CursorCommands.GroupLeft,
            (ConsoleModifiers.Control, ConsoleKey.RightArrow) => CursorCommands.GroupRight,
            (ConsoleModifiers.Control, ConsoleKey.Home) => CursorCommands.DocStart,
            (ConsoleModifiers.Control, ConsoleKey.End) => CursorCommands.DocEnd,
            (0, ConsoleKey.PageUp) => t => CursorCommands.PageUp(t, 20),
            (0, ConsoleKey.PageDown) => t => CursorCommands.PageDown(t, 20),

            (0, ConsoleKey.Enter) => LineCommands.InsertNewlineAndIndent,
            (0, ConsoleKey.Backspace) => DeleteCommands.CharBackward,
            (0, ConsoleKey.Delete) => DeleteCommands.CharForward,
            (0, ConsoleKey.Tab) => LineCommands.InsertTab,

            (ConsoleModifiers.Control, ConsoleKey.A) => SelectionCommands.SelectAll,
            (ConsoleModifiers.Control, ConsoleKey.Z) => t => false,

            (ConsoleModifiers.Control, ConsoleKey.Y) => t => false,

            (ConsoleModifiers.Control, ConsoleKey.K) => DeleteCommands.ToLineEnd,
            (0, ConsoleKey.Escape) => SelectionCommands.SimplifySelection,

            _ when key.KeyChar >= ' ' && key.Modifiers == 0 => InsertChar(key.KeyChar),

            _ => null,
        };
    }

    private static StateCommand InsertChar(char ch) => t =>
    {
        var s = t.State;
        if (s.ReadOnly) return false;

        var (changes, selection) = ChangeHelpers.InsertPerRange(s.Selection.Ranges, _ => ch.ToString());
        var newState = s.Apply(new TransactionSpec
        {
            Changes = changes,
            Selection = selection,
            ScrollIntoView = true,
            UserEvent = "input.type"
        });
        t.Apply(newState);
        return true;
    };

    private void Redraw(IReadOnlyList<string> sidebarLines)
    {
        var layout = _pane.Layout();
        for (var row = 0; row < layout.ContentHeight; row++)
        {
            var sidebar = row < sidebarLines.Count ? sidebarLines[row] : "";
            _screen.WriteRow(row, 0, AnsiText.Fit(sidebar, layout.SidebarWidth));
            _screen.WriteRow(row, layout.SidebarWidth, "│");
        }
        _screen.WriteRow(layout.ContentHeight, 0, new string('─', _screen.Width));

        _stateCell.Notify();
        _world.FlushReactive();
    }

    public void Dispose()
    {
        _mount.Unmount();
        _world.Dispose();
        Context<World>.Current = _prevContext;
        (_completionProvider as IDisposable)?.Dispose();
    }
}

#endif
