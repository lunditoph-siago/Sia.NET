namespace Sia_Examples.Notebook;

public readonly record struct NotebookViewProps(
    INotebookView View,
    NotebookSessionSnapshot Snapshot,
    NotebookCellState InitialCellState);
