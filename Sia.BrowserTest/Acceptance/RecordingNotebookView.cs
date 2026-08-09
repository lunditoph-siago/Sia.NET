using Sia_Examples;
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class RecordingNotebookView : INotebookView
{
    public List<NotebookCellSnapshot> CellUpserts { get; } = [];

    public List<NotebookCellSnapshot> CellRemovals { get; } = [];

    public List<PackageView> PackageUpserts { get; } = [];

    public List<PackageView> PackageRemovals { get; } = [];

    public List<int> PackageCounts { get; } = [];

    public void Clear()
    {
        CellUpserts.Clear();
        CellRemovals.Clear();
        PackageUpserts.Clear();
        PackageRemovals.Clear();
        PackageCounts.Clear();
    }

    void IRenderHost<NotebookCellSnapshot>.Upsert(in NotebookCellSnapshot view)
        => CellUpserts.Add(view);

    void IRenderHost<NotebookCellSnapshot>.Remove(in NotebookCellSnapshot view)
        => CellRemovals.Add(view);

    void IRenderHost<PackageView>.Upsert(in PackageView view)
        => PackageUpserts.Add(view);

    void IRenderHost<PackageView>.Remove(in PackageView view)
        => PackageRemovals.Add(view);

    void IRenderHost<PackageCountView>.Upsert(in PackageCountView view)
        => PackageCounts.Add(view.Count);

    void IRenderHost<PackageCountView>.Remove(in PackageCountView view)
        => PackageCounts.Add(0);
}
