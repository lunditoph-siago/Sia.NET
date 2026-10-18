namespace Sia_Examples.Editor;

public readonly record struct EditorMemento(
    EditorSelection Selection,
    int ViewLine,
    int ViewColumn,
    double ViewOffset,
    double ScrollTop,
    double ScrollLeft)
{
    internal BrowserEditorHost.HistorySnapshot? History { get; init; }
}
