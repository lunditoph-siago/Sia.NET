namespace Sia_Examples.Notebook;

public sealed record CellFloatingHost(
    string Id,
    CellNode Root,
    int X,
    int Y,
    int Width,
    int Height);
