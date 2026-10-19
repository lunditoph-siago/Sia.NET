namespace Sia.Examples.Notebook;

public sealed record CodeCellBlock(
    string Id,
    IReadOnlyList<CellScript> Scripts,
    bool Editable,
    string? Scope) : NotebookBlock;
