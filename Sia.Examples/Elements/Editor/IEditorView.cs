namespace Sia_Examples.Editor;

public interface IEditorView
{
    int VisibleLines { get; }
    void BeginRender();
    void RenderGutter(int screenRow, int lineIndex, bool isCursorLine);
    void RenderLine(int screenRow, string text, CursorState cursor, int lineIndex);
    void RenderCursor(int screenRow, int column);
    void RenderStatus(string left, string right);
    void EndRender();
    void Dispose();
}
