namespace Sia_Examples.Notebook;

public sealed record ListBlock(IReadOnlyList<IReadOnlyList<Inline>> Items) : NotebookBlock;
