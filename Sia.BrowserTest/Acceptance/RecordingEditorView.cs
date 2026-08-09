using Sia_Examples;
using Sia_Examples.Editor;

namespace Sia_BrowserTest.Acceptance;

public sealed class RecordingEditorView : IEditorView
{
    public List<EditorLineView> LineUpserts { get; } = [];

    public List<EditorLineView> LineRemovals { get; } = [];

    public List<EditorActiveLineView> ActiveLineUpserts { get; } = [];

    public List<EditorActiveLineView> ActiveLineRemovals { get; } = [];

    public int SelectionUpserts { get; private set; }

    public int StatusUpserts { get; private set; }

    public void Clear()
    {
        LineUpserts.Clear();
        LineRemovals.Clear();
        ActiveLineUpserts.Clear();
        ActiveLineRemovals.Clear();
        SelectionUpserts = 0;
        StatusUpserts = 0;
    }

    public void SuppressNextSelectionUpdate()
    {
    }

    public void PreserveNativeEdit(int lineIdentity)
    {
    }

    public void Dispose()
    {
    }

    void IRenderHost<EditorLineView>.Upsert(in EditorLineView view)
        => LineUpserts.Add(view);

    void IRenderHost<EditorLineView>.Remove(in EditorLineView view)
        => LineRemovals.Add(view);

    void IRenderHost<EditorActiveLineView>.Upsert(in EditorActiveLineView view)
        => ActiveLineUpserts.Add(view);

    void IRenderHost<EditorActiveLineView>.Remove(in EditorActiveLineView view)
        => ActiveLineRemovals.Add(view);

    void IRenderHost<EditorSelectionView>.Upsert(in EditorSelectionView view)
        => SelectionUpserts++;

    void IRenderHost<EditorSelectionView>.Remove(in EditorSelectionView view)
    {
    }

    void IRenderHost<EditorStatusView>.Upsert(in EditorStatusView view)
        => StatusUpserts++;

    void IRenderHost<EditorStatusView>.Remove(in EditorStatusView view)
    {
    }
}
