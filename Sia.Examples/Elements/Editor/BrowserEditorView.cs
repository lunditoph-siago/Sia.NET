#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace Sia_Examples.Editor;

public sealed class BrowserEditorView(string containerId) : IEditorView
{
    private BrowserElement _gutter = null!;
    private BrowserElement _content = null!;
    private int _visibleLines = 15;

    public int VisibleLines => _visibleLines;

    public void FindElements()
    {
        _gutter = BrowserElement.Find(containerId + "-gutter");
        _content = BrowserElement.Find(containerId + "-content");
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

    public void RenderLine(int screenRow, string text, CursorState cursor, int lineIndex)
    {
        var lineEl = BrowserElement.Create("span").Class("editor-line");
        if (lineIndex == cursor.Line)
            lineEl.ToggleClass("cursor-line", true);

        var escaped = EscapeHtml(text);
        if (escaped.Length == 0)
            escaped = " ";

        BrowserDom.SetInnerHtml(lineEl.Handle, escaped);
        _content.Append(lineEl);

    }

    public void RenderCursor(int screenRow, int column) { }
    public void RenderStatus(string left, string right) { }
    public void EndRender() { }

    public void SetVisibleLines(int lines)
    {
        _visibleLines = Math.Max(lines, 5);
    }

    public void Dispose()
    {
        _gutter?.Dispose();
        _content?.Dispose();
    }

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
