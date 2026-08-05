#if !BROWSER
using Sia;
using Sia.Reactive;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class ConsoleEditorHost : IDisposable
{
    private readonly ConsoleScreen _screen;
    private readonly SplitPaneRenderer _pane;
    private readonly ConsoleEditorView _view;
    private readonly World _world;
    private readonly World? _prevContext;
    private readonly ReactiveMount<CellEditorProps> _mount;
    private readonly string _cellId;
    private readonly int _gutterWidth = 5;
    private readonly IEditorCompletionProvider _completionProvider;

    private State<EditorDoc> _docState;
    private State<CursorState> _cursorState;
    private bool _saved;

    private CompletionQueryResult? _completion;
    private int _completionIndex;

    public ConsoleEditorHost(
        ConsoleScreen screen, string cellId, string initialSource, IMetadataReferenceProvider references)
    {
        _screen = screen;
        _pane = new SplitPaneRenderer(screen);
        _cellId = cellId;
        _completionProvider = new RoslynCompletionProvider(references);

        var layout = _pane.Layout();
        _view = new ConsoleEditorView(screen,
            layout.WorkspaceCol, 0,
            layout.WorkspaceWidth, layout.ContentHeight,
            _gutterWidth);

        _world = new World();
        _prevContext = Context<World>.Current;
        Context<World>.Current = _world;

        var props = new CellEditorProps(cellId, initialSource, _view);
        _mount = _world.Mount(CellEditor.Definition, props);

        _docState = _mount.GetState<EditorDoc>(0);
        _cursorState = _mount.GetState<CursorState>(1);
    }

    public string? Edit(IReadOnlyList<string> sidebarLines, ref int sidebarScroll)
    {
        _saved = false;
        Redraw(sidebarLines);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var cursor = _cursorState.Value;

            if (_completion is { } open)
            {
                if (key.Key == ConsoleKey.Escape)
                {
                    _completion = null;
                    _docState.Notify();
                }
                else if (HandleCompletionKey(key, open))
                {
                    continue;
                }
            }

            var cmd = ConsoleKeyMap.Map(key, cursor.Mode);

            if (cmd is SaveCommand)
            {
                _saved = true;
                return _docState.Value.Value.FullText;
            }

            if (cursor.Mode == EditorMode.Normal && key.Key == ConsoleKey.Oem1)
            {
                var result = HandleColonCommand(sidebarLines);
                if (result == ColonResult.Exit)
                    return _saved ? _docState.Value.Value.FullText : null;
                if (result == ColonResult.Save)
                    return _docState.Value.Value.FullText;
                Redraw(sidebarLines);
                continue;
            }

            if (cmd == null) continue;

            var (newDoc, newCursor) = CommandBridge.Apply(
                cmd, _docState.Value, _cursorState.Value);

            if (cursor.Mode == EditorMode.Visual &&
                newCursor.Mode != EditorMode.Visual)
            {
                newCursor = newCursor.WithoutSelection();
            }

            newCursor = EnsureCursorVisible(newDoc, newCursor);

            _docState.Set(newDoc);
            _cursorState.Set(newCursor);

            UpdateCompletion(cmd, cursor.Mode, newDoc, newCursor);

            _world.FlushReactive();
            DrawCompletionPopup();
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

    private void UpdateCompletion(ICommand cmd, EditorMode modeBeforeCommand, EditorDoc newDoc, CursorState newCursor)
    {
        var shouldQuery = modeBeforeCommand == EditorMode.Insert
            && newCursor.Mode == EditorMode.Insert
            && (cmd is DeleteLeftCommand
                || cmd is InsertCharCommand { Char: var ch } && (char.IsLetterOrDigit(ch) || ch is '_' or '.'));

        if (!shouldQuery)
        {
            _completion = null;
            return;
        }

        var offset = ToOffset(newDoc.Value, newCursor.Line, newCursor.Column);
        var result = _completionProvider.QueryAsync(newDoc.Value.FullText, offset)
            .GetAwaiter().GetResult();

        _completion = result.IsOpen ? result : null;
        _completionIndex = 0;
    }

    private void AcceptCompletion(CompletionQueryResult completion)
    {
        var item = completion.Items[_completionIndex];
        _completion = null;

        var doc = _docState.Value;
        doc.Mutate(d =>
        {
            var (sl, sc) = FromOffset(d, item.ReplaceStart);
            var (el, ec) = FromOffset(d, item.ReplaceEnd);
            d.DeleteRange(sl, sc, el, ec);
            d.Insert(sl, sc, item.InsertText);
        });

        var (nl, nc) = FromOffset(doc.Value, item.ReplaceStart + item.InsertText.Length);
        var newCursor = EnsureCursorVisible(doc, _cursorState.Value with { Line = nl, Column = nc, PreferredColumn = nc });

        _docState.Set(doc);
        _cursorState.Set(newCursor);
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
        _pane.RenderFloatingAt(_view.CursorScreenRow, _view.CursorScreenCol, lines, width);
    }

    private static int ToOffset(EditorDocument doc, int line, int col)
    {
        var offset = 0;
        for (var i = 0; i < line; i++) offset += doc.LineLength(i) + 1;
        return offset + col;
    }

    private static (int Line, int Col) FromOffset(EditorDocument doc, int offset)
    {
        var remaining = offset;
        for (var i = 0; i < doc.LineCount; i++)
        {
            var len = doc.LineLength(i);
            if (remaining <= len) return (i, remaining);
            remaining -= len + 1;
        }
        var last = doc.LineCount - 1;
        return (last, doc.LineLength(last));
    }

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

        _docState.Notify();
        _world.FlushReactive();
    }

    private enum ColonResult { None, Exit, Save }

    private ColonResult HandleColonCommand(IReadOnlyList<string> sidebarLines)
    {
        var layout = _pane.Layout();
        _screen.WriteRow(layout.InputRow, 0, AnsiText.Fit(":", _screen.Width));
        _screen.ShowCursorAt(layout.InputRow, 1);

        var input = ReadColonInput();
        return input switch
        {
            "w" => HandleSave(),
            "q" => ColonResult.Exit,
            "wq" => HandleSave(),
            "q!" => HandleForceQuit(),
            _ => ColonResult.None,
        };
    }

    private ColonResult HandleSave()
    {
        _saved = true;
        return ColonResult.Save;
    }

    private ColonResult HandleForceQuit()
    {
        return ColonResult.Exit;
    }

    private string ReadColonInput()
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) return sb.ToString().Trim();
            if (key.Key == ConsoleKey.Escape) return "";
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (key.KeyChar >= ' ') sb.Append(key.KeyChar);
        }
    }

    private CursorState EnsureCursorVisible(EditorDoc docComp, CursorState cursor)
    {
        var layout = _pane.Layout();
        var maxScroll = Math.Max(docComp.LineCount - layout.ContentHeight + 1, 0);
        var scrollLine = Math.Clamp(cursor.ScrollLine, 0, maxScroll);

        if (cursor.Line < scrollLine)
            scrollLine = cursor.Line;
        if (cursor.Line >= scrollLine + layout.ContentHeight)
            scrollLine = cursor.Line - layout.ContentHeight + 1;
        scrollLine = Math.Clamp(scrollLine, 0, maxScroll);

        return cursor with { ScrollLine = scrollLine };
    }

    public void Dispose()
    {
        _mount.Unmount();
        _world.Dispose();
        Context<World>.Current = _prevContext;
    }
}
#endif
