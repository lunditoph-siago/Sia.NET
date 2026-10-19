using System.Collections.Immutable;

namespace Sia.Examples.Notebook;

public readonly record struct NotebookSessionSnapshot(
    int Version,
    ImmutableArray<NotebookCellSnapshot> Cells,
    ImmutableArray<PackageStatus> Packages);
