namespace Sia_Examples.Notebook;

public sealed record DockSplit(
    string Id,
    DockAxis Axis,
    double Ratio,
    DockNode First,
    DockNode Second) : DockNode(Id);
