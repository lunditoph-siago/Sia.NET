namespace Sia.Examples.Notebook;

public sealed record CellWindow(
    string Id,
    string CellId,
    string HomeRegionId,
    CellWindowKind Kind,
    string Title,
    string SourceId);
