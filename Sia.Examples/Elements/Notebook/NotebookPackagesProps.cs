using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public readonly record struct NotebookPackagesProps(
    INotebookView View,
    ImmutableArray<PackageStatus> Packages);
