using Sia_Examples.Dom;

namespace Sia_Examples.Editor;

internal interface IEditorLinesOwner : IDisposable
{
    bool Apply(in EditorLinesView view, IReadOnlySet<int> identities);
    void Restore();
    void SetActiveLine(int? identity);
    void Clear();
}

internal interface IEditorLinesOwnerFactory
{
    IEditorLinesOwner CreateEditorLines(
        DomElement gutter, DomElement lines, DomElement gutterBefore,
        DomElement gutterAfter, DomElement linesBefore, DomElement linesAfter);
}
