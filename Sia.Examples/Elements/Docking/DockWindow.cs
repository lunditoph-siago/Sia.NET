namespace Sia_Examples.Notebook;

public sealed record DockWindow(
    string Id,
    string CellId,
    string HomeRegionId,
    DockWindowKind Kind,
    string Title);
