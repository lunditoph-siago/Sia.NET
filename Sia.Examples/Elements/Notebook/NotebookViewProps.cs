namespace Sia.Examples.Notebook;

public readonly record struct NotebookViewProps(
    INotebookView View,
    NotebookSessionSnapshot Snapshot,
    NotebookCellState InitialCellState);
