namespace Sia.Examples.Notebook;

public sealed record CellSplit(
    string Id,
    CellAxis Axis,
    double Ratio,
    CellNode First,
    CellNode Second) : CellNode(Id);
