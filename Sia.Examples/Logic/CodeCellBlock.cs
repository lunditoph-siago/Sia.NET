namespace Sia_Examples.Notebook;

public sealed record CodeCellBlock(
    string Id,
    string InitialSource,
    bool Editable,
    string? Scope) : NotebookBlock;
