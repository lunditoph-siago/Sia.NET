namespace Sia_Examples.Notebook;

public sealed record ListBlock(string Id, IReadOnlyList<IReadOnlyList<Inline>> Items) : NotebookBlock;
