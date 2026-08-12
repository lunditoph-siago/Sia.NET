namespace Sia_Examples.Notebook;

public interface INotebookView :
    IRenderHost<NotebookCellSnapshot>,
    IRenderHost<PackageView>,
    IRenderHost<PackageCountView>,
    IRenderHost<NotebookCellPresentation>
{
}
