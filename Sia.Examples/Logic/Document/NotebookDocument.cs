namespace Sia_Examples.Notebook;

public sealed record NotebookDocument(string Title, IReadOnlyList<PackageRef> Packages, IReadOnlyList<NotebookSection> Sections);
