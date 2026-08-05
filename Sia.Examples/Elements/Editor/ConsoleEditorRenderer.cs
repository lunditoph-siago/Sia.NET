#if !BROWSER
using System.Text;

namespace Sia_Examples.Editor;

public sealed class ConsoleEditorView(ConsoleScreen screen, int left, int top, int width, int height, int gutterWidth = 5)
    : IEditorView
{
    private const string Reset = "\e[0m";
    private const string Dim = "\e[2m";
    private const string Bold = "\e[1m";
    private const string InsertBg = "\e[48;5;22m";
    private const string NormalBg = "\e[48;5;24m";
    private const string VisualBg = "\e[48;5;54m";
    private const string SelectionBg = "\e[48;5;236m";

    private readonly int _width = Math.Max(width, 20);
    private int _cursorScreenRow = -1;
    private int _cursorScreenCol;

    public int VisibleLines { get; } = Math.Max(height, 3);

    public void BeginRender()
    {
        for (var i = 0; i < VisibleLines; i++)
            screen.WriteRow(top + i, left, new string(' ', _width));
        screen.WriteRow(top + VisibleLines, 0, new string(' ', screen.Width));
        _cursorScreenRow = -1;
    }

    public void RenderGutter(int screenRow, int lineIndex, bool isCursorLine)
    {
        var num = (lineIndex + 1).ToString().PadLeft(gutterWidth - 2);
        var gutter = isCursorLine
            ? $"{Bold} {num} {Reset}"
            : $"{Dim} {num} {Reset}";
        screen.WriteRow(top + screenRow, left, gutter);
    }

    public void RenderLine(int screenRow, string text, CursorState cursor, int lineIndex, List<Sia_Examples.Notebook.HighlightRun> highlights, int lineStartOffset)
    {
        var ansi = text.Length > 0
            ? BuildAnsiLine(text, highlights, lineStartOffset)
            : " ";
        var styled = ApplySelection(ansi, lineIndex, cursor);
        screen.WriteRow(top + screenRow, left + gutterWidth,
            AnsiText.Fit(styled, _width - gutterWidth));
    }

    private static string BuildAnsiLine(string text, List<Sia_Examples.Notebook.HighlightRun> runs, int lineStart)
    {
        if (text.Length == 0) return " ";
        var lineEnd = lineStart + text.Length;
        var sb = new StringBuilder();
        var cursor = 0;
        foreach (var run in runs)
        {
            if (run.Start + run.Length <= lineStart) continue;
            if (run.Start >= lineEnd) break;
            var rs = Math.Max(run.Start, lineStart) - lineStart;
            var re = Math.Min(run.Start + run.Length, lineEnd) - lineStart;
            if (rs > cursor) sb.Append(text[cursor..rs]);
            var color = AnsiColor(run.Classification);
            if (color >= 0) { sb.Append("\e[38;5;").Append(color).Append('m'); }
            sb.Append(text[rs..re]);
            if (color >= 0) sb.Append(Reset);
            cursor = re;
        }
        if (cursor < text.Length) sb.Append(text[cursor..]);
        return sb.ToString();
    }

    private static int AnsiColor(string classification) => classification switch
    {
        "keyword" or "control keyword" or "preprocessor keyword" => 183,
        "string literal" => 150,
        "comment" => 103,
        "numeric literal" => 216,
        _ => -1,
    };

    public void RenderCursor(int screenRow, int column)
    {
        _cursorScreenRow = top + screenRow;
        _cursorScreenCol = left + gutterWidth + column;
    }

    public void RenderStatus(string statusLeft, string statusRight)
    {
        var row = top + VisibleLines;
        var padding = new string(' ', Math.Max(1, _width - statusLeft.Length - statusRight.Length));
        screen.WriteRow(row, left, AnsiText.Fit(statusLeft + padding + statusRight, _width));
    }

    public void EndRender()
    {
        if (_cursorScreenRow >= 0)
            screen.ShowCursorAt(_cursorScreenRow, _cursorScreenCol);
    }

    public void Dispose()
    {
        screen.HideCursor();
    }

    private string ApplySelection(string line, int lineIndex, CursorState cursor)
    {
        if (!cursor.HasSelection) return line;
        var (sl, sc) = cursor.SelectionStart;
        var (el, ec) = cursor.SelectionEnd;
        if (lineIndex < sl || lineIndex > el) return line;
        if (lineIndex == sl && lineIndex == el)
        {
            var start = Math.Min(sc, ec);
            var end = Math.Min(Math.Max(sc, ec), line.Length);
            if (start >= line.Length) return line;
            return line[..start] + SelectionBg + line[start..end] + Reset;
        }
        if (lineIndex == sl)
        {
            sc = Math.Min(sc, line.Length);
            return line[..sc] + SelectionBg + line[sc..] + Reset;
        }
        if (lineIndex == el)
        {
            ec = Math.Min(ec, line.Length);
            return SelectionBg + line[..ec] + Reset + line[ec..];
        }
        return SelectionBg + line + Reset;
    }
}
#endif
