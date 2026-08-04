#if !BROWSER
using System.Text;
using System.Text.RegularExpressions;

namespace Sia_Examples;

internal static partial class AnsiText
{
    [GeneratedRegex("\\[[0-9;]*m")]
    private static partial Regex EscapeSequence();

    public static int VisibleLength(string s) => EscapeSequence().Replace(s, "").Length;

    public static string Fit(string s, int width)
    {
        if (width <= 0) {
            return "";
        }
        var visible = VisibleLength(s);
        if (visible <= width) {
            return s + new string(' ', width - visible) + "[0m";
        }

        var sb = new StringBuilder();
        var count = 0;
        var i = 0;
        while (i < s.Length && count < width) {
            var m = EscapeSequence().Match(s, i);
            if (m.Success && m.Index == i) {
                sb.Append(m.Value);
                i += m.Length;
                continue;
            }
            sb.Append(s[i]);
            count++;
            i++;
        }
        sb.Append("[0m");
        return sb.ToString();
    }
}

internal sealed class ConsoleScreen : IDisposable
{
    public ConsoleScreen()
    {
        Console.Write("[?1049h[?25l[2J");
    }

    public int Width => Math.Max(Console.WindowWidth, 60);
    public int Height => Math.Max(Console.WindowHeight, 15);

    public void WriteRow(int row, int col, string text)
        => Console.Write($"[{row + 1};{col + 1}H{text}");

    public void ShowCursorAt(int row, int col)
        => Console.Write($"[{row + 1};{col + 1}H[?25h");

    public void HideCursor() => Console.Write("[?25l");

    public void Dispose()
    {
        Console.Write("[?25h[?1049l");
    }
}

internal readonly record struct SplitLayout(int SidebarWidth, int WorkspaceCol, int WorkspaceWidth, int ContentHeight, int InputRow);

internal sealed class SplitPaneRenderer(ConsoleScreen screen)
{
    public SplitLayout Layout()
    {
        var sidebarWidth = Math.Clamp(screen.Width / 3, 22, 34);
        var workspaceCol = sidebarWidth + 1;
        var workspaceWidth = Math.Max(screen.Width - workspaceCol - 1, 20);
        var contentHeight = Math.Max(screen.Height - 2, 5);
        return new(sidebarWidth, workspaceCol, workspaceWidth, contentHeight, screen.Height - 1);
    }

    public void Render(IReadOnlyList<string> sidebarLines, IReadOnlyList<string> workspaceLines, string statusLine)
    {
        var layout = Layout();

        for (var row = 0; row < layout.ContentHeight; row++) {
            var sidebar = row < sidebarLines.Count ? sidebarLines[row] : "";
            screen.WriteRow(row, 0, AnsiText.Fit(sidebar, layout.SidebarWidth));
            screen.WriteRow(row, layout.SidebarWidth, "│");

            var workspace = row < workspaceLines.Count ? workspaceLines[row] : "";
            screen.WriteRow(row, layout.WorkspaceCol, AnsiText.Fit(workspace, layout.WorkspaceWidth));
        }

        screen.WriteRow(layout.ContentHeight, 0, new string('─', screen.Width));
        screen.WriteRow(layout.InputRow, 0, AnsiText.Fit(statusLine, screen.Width));
    }

    public void RenderFloatingAt(int anchorRow, int anchorCol, IReadOnlyList<string> lines, int width)
    {
        var height = lines.Count + 2;
        var left = Math.Clamp(anchorCol, 0, Math.Max(screen.Width - width - 2, 0));
        var top = anchorRow + height < screen.Height ? anchorRow + 1 : Math.Max(anchorRow - height, 0);

        screen.WriteRow(top, left, "┌" + new string('─', width) + "┐");
        for (var i = 0; i < lines.Count; i++) {
            screen.WriteRow(top + 1 + i, left, "│" + AnsiText.Fit(lines[i], width) + "│");
        }
        screen.WriteRow(top + height - 1, left, "└" + new string('─', width) + "┘");
    }
}
#endif
