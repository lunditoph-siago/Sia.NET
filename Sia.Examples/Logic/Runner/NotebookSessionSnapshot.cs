using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public readonly record struct NotebookSessionSnapshot(
    int Version,
    ImmutableArray<NotebookCellSnapshot> Cells,
    ImmutableArray<PackageStatus> Packages);
