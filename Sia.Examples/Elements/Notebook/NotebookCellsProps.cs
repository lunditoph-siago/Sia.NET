using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public readonly record struct NotebookCellsProps(
    INotebookView View,
    ImmutableArray<NotebookCellSnapshot> Cells);
