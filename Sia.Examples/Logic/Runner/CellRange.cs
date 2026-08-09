namespace Sia_Examples.Notebook;

public readonly record struct CellRange(
    string CellId,
    int StatementsStartLine,
    int? TypesStartLine,
    string StartToken,
    string EndToken);
