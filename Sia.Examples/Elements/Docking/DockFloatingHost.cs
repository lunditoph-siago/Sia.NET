namespace Sia_Examples.Notebook;

public sealed record DockFloatingHost(
    string Id,
    DockNode Root,
    int X,
    int Y,
    int Width,
    int Height);
