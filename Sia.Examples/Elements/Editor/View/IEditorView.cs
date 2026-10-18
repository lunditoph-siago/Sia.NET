namespace Sia_Examples.Editor;

public interface IEditorView :
    IRenderHost<EditorLinesView>,
    IRenderHost<EditorActiveLineView>,
    IRenderHost<EditorSelectionView>,
    IRenderHost<EditorStatusView>,
    IRenderHost<EditorDocumentView>,
    IDisposable
{
    public void SuppressNextSelectionUpdate();

    public void SetSpacerHeights(double beforePx, double afterPx);
}
