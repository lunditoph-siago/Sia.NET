using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

internal interface IEditorLinesOwner : IDisposable
{
    public bool Apply(in EditorLinesView view, IReadOnlySet<int> identities);
    public void Restore();
    public void SetActiveLine(int? identity);
    public void Clear();
}

internal interface IEditorLinesOwnerFactory
{
    public IEditorLinesOwner CreateEditorLines(
        DomElement gutter, DomElement lines, DomElement gutterBefore,
        DomElement gutterAfter, DomElement linesBefore, DomElement linesAfter);
}
