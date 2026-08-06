#if !BROWSER
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class ConsoleTermRenderer : IEditorRenderer
{
    private readonly ConsoleScreen _screen;
    private readonly int _left, _top, _width, _gutterWidth;
    private readonly ViewportTracker _viewport;
    private int _cursorRow = -1, _cursorCol;

    public int VisibleLines { get; }

    public int CursorScreenRow => _cursorRow;
    public int CursorScreenCol => _cursorCol;

    public ConsoleTermRenderer(ConsoleScreen screen, int left, int top,
        int width, int height, int gutterWidth = 5)
    {
        _screen = screen; _left = left; _top = top;
        _width = Math.Max(width, 20); _gutterWidth = gutterWidth;
        VisibleLines = Math.Max(height, 3);
        _viewport = new ViewportTracker(VisibleLines);
    }

    public ViewportState AdvanceViewport(int cursorLine, int totalLines) => _viewport.Advance(cursorLine, totalLines);

    public void BeginRender()
    {
        _cursorRow = -1;
    }

    public void RenderGutter(int screenRow, int lineIndex, bool isCursorLine)
    {
        var num = (lineIndex + 1).ToString().PadLeft(_gutterWidth - 2);
        var gutter = isCursorLine
            ? $"\e[1m {num} \e[0m"
            : $"\e[2m {num} \e[0m";
        _screen.WriteRow(_top + screenRow, _left, gutter);
    }

    public void RenderLine(int screenRow, int lineIndex, string text, IReadOnlyList<StyledRun>? runs, string? lineClass)
    {
        var width = _width - _gutterWidth;
        var display = runs is null || runs.Count == 0 ? (text.Length > 0 ? text : " ") : BuildAnsi(runs);
        _screen.WriteRow(_top + screenRow, _left + _gutterWidth, AnsiText.Fit(display, width));
    }

    private static string BuildAnsi(IReadOnlyList<StyledRun> runs)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in runs) {
            var color = run.Class is null ? null : CSharpHighlighter.AnsiColor(run.Class);
            if (color is { } code) sb.Append("\e[38;5;").Append(code).Append('m').Append(run.Text).Append("\e[0m");
            else sb.Append(run.Text);
        }
        return sb.ToString();
    }

    public void RenderSelection(int anchorLineIndex, int anchorCol, int headLineIndex, int headCol, bool empty)
    {
        var vp = _viewport.Current;
        _cursorRow = _top + (headLineIndex + 1 - vp.From);
        _cursorCol = _left + _gutterWidth + headCol;
    }

    public void RenderStatus(string left, string right)
    {
        var padding = new string(' ', Math.Max(1, _width - left.Length - right.Length));
        var text = left + padding + right;
        _screen.WriteRow(_top + VisibleLines, _left, AnsiText.Fit(text, _width));
    }

    public void EndRender()
    {
        if (_cursorRow >= 0) _screen.ShowCursorAt(_cursorRow, _cursorCol);
    }

    public void Dispose() { _screen.HideCursor(); }
}
#endif
