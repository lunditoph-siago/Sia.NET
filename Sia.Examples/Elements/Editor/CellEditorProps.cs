namespace Sia_Examples.Editor;

public readonly record struct CellEditorProps(string CellId, string InitialSource, IEditorView View)
{
    public IEditorView View { get; init; } = View ?? throw new ArgumentNullException(nameof(View));
}
