#if !BROWSER
using Sia;
using Sia.Reactive;

namespace Sia_Examples.Editor;

public sealed class ConsoleEditorHost : IDisposable
{
    private readonly ConsoleScreen _screen;
    private readonly SplitPaneRenderer _pane;
    private readonly ConsoleEditorView _view;
    private readonly World _world;
    private readonly ReactiveMount<CellEditorProps> _mount;
    private readonly string _cellId;
    private readonly int _gutterWidth = 5;

    private State<EditorDoc> _docState;
    private State<CursorState> _cursorState;
    private bool _saved;

    public ConsoleEditorHost(ConsoleScreen screen, string cellId, string initialSource)
    {
        _screen = screen;
        _pane = new SplitPaneRenderer(screen);
        _cellId = cellId;

        var layout = _pane.Layout();
        _view = new ConsoleEditorView(screen,
            layout.WorkspaceCol, 0,
            layout.WorkspaceWidth, layout.ContentHeight,
            _gutterWidth);

        _world = new World();
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

            _world.FlushReactive();
        }
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
        _saved = true;
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
    }
}
#endif
