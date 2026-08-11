namespace Sia_Examples.Notebook;

public sealed record ParagraphBlock(
    string Id,
    IReadOnlyList<Inline> Inlines,
    bool Editable) : NotebookBlock;
