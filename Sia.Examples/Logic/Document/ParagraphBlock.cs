namespace Sia.Examples.Notebook;

public sealed record ParagraphBlock(
    string Id,
    IReadOnlyList<Inline> Inlines,
    bool Editable) : NotebookBlock;
