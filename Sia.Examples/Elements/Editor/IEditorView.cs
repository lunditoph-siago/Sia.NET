namespace Sia_Examples.Editor;

public interface IEditorView :
    IRenderHost<EditorLineView>,
    IRenderHost<EditorActiveLineView>,
    IRenderHost<EditorSelectionView>,
    IRenderHost<EditorStatusView>,
    IDisposable
{
    public void SuppressNextSelectionUpdate();

    public void PreserveNativeEdit(int lineIdentity);
}
