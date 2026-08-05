#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorView : IEditorView
{
    private readonly string _containerId;
    private BrowserElement _gutter = null!;
    private BrowserElement _content = null!;
    private int _visibleLines = 15;

    public int VisibleLines => _visibleLines;

    public string CursorLineElementId => _containerId + "-cursor-line";

    public BrowserEditorView(string containerId)
    {
        _containerId = containerId;
    }

    public void FindElements()
    {
        _gutter = BrowserElement.Find(_containerId + "-gutter");
        _content = BrowserElement.Find(_containerId + "-content");
    }

    public void BeginRender()
    {
        _gutter.Text("");
        _content.Text("");
    }

    public void RenderGutter(int screenRow, int lineIndex, bool isCursorLine)
    {
        var num = BrowserElement.Create("div").Class("editor-gutter-line");
        num.Text((lineIndex + 1).ToString());
        if (isCursorLine) num.ToggleClass("active", true);
        _gutter.Append(num);
    }

    public void RenderLine(int screenRow, string text, CursorState cursor, int lineIndex, List<HighlightRun> highlights, int lineStartOffset)
    {
        var lineEl = BrowserElement.Create("span").Class("editor-line");
        if (lineIndex == cursor.Line)
        {
            lineEl.ToggleClass("cursor-line", true);
            lineEl.Id(CursorLineElementId);
        }

        var html = BuildHighlightedHtml(text, highlights, lineStartOffset);
        BrowserDom.SetInnerHtml(lineEl.Handle, html);
        _content.Append(lineEl);
    }

    public void RenderCursor(int screenRow, int column) { }
    public void RenderStatus(string left, string right) { }
    public void EndRender() { }

    public void SetVisibleLines(int lines)
    {
        _visibleLines = Math.Max(lines, 5);
    }

    public void Dispose() { }

    private static string BuildHighlightedHtml(string text, List<HighlightRun> runs, int lineStart)
    {
        if (text.Length == 0) return " ";
        var lineEnd = lineStart + text.Length;
        var sb = new System.Text.StringBuilder();
        var cursor = 0;
        foreach (var run in runs)
        {
            if (run.Start + run.Length <= lineStart) continue;
            if (run.Start >= lineEnd) break;
            var rs = Math.Max(run.Start, lineStart) - lineStart;
            var re = Math.Min(run.Start + run.Length, lineEnd) - lineStart;
            if (rs > cursor)
                sb.Append(EscapeHtml(text[cursor..rs]));
            var cls = CssClass(run.Classification);
            sb.Append("<span class=\"").Append(cls).Append("\">");
            sb.Append(EscapeHtml(text[rs..re]));
            sb.Append("</span>");
            cursor = re;
        }
        if (cursor < text.Length)
            sb.Append(EscapeHtml(text[cursor..]));
        return sb.ToString();
    }

    private static string CssClass(string classification)
        => CSharpHighlighter.CssClass(classification);

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}

internal static partial class BrowserDom
{
    [JSImport("setInnerHtml", "main.js")]
    internal static partial void SetInnerHtml(JSObject element, string html);
}
#endif
