namespace Sia_Examples.Editor;

public interface IEditorView :
    IRenderHost<EditorLineView>,
    IRenderHost<EditorActiveLineView>,
    IRenderHost<EditorSelectionView>,
    IRenderHost<EditorStatusView>,
    IRenderHost<EditorDocumentView>,
    IDisposable
{
    public void SuppressNextSelectionUpdate();

    public void PreserveNativeEdit(int lineIdentity);

    public void ScrollLineIntoView(double targetTop);

    public void SetSpacerHeights(double beforePx, double afterPx);
}
