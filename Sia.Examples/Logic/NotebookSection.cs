namespace Sia_Examples.Notebook;

public sealed record NotebookSection(string Title, IReadOnlyList<NotebookBlock> Blocks);
