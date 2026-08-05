using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

public interface IEditorView
{
    int VisibleLines { get; }
    void BeginRender();
    void RenderGutter(int screenRow, int lineIndex, bool isCursorLine);
    void RenderLine(int screenRow, string text, CursorState cursor, int lineIndex, List<HighlightRun> highlights, int lineStartOffset);
    void RenderCursor(int screenRow, int column);
    void RenderStatus(string left, string right);
    void EndRender();
    void Dispose();
}
