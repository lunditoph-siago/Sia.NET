using System.Collections.Immutable;

namespace Sia.Examples.Notebook;

public readonly record struct NotebookCellsProps(
    INotebookView View,
    ImmutableArray<NotebookCellSnapshot> Cells);
