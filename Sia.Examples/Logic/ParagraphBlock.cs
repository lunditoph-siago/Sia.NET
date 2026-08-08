namespace Sia_Examples.Notebook;

public sealed record ParagraphBlock(IReadOnlyList<Inline> Inlines) : NotebookBlock;
