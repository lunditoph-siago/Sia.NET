namespace Sia.Examples.Notebook;

public sealed record NotebookSection(string Id, string Title, IReadOnlyList<NotebookBlock> Blocks);
