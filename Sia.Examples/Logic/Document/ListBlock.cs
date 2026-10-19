namespace Sia.Examples.Notebook;

public sealed record ListBlock(string Id, IReadOnlyList<IReadOnlyList<Inline>> Items) : NotebookBlock;
