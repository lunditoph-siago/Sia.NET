using Sia.Reactive;

namespace Sia_Examples.Notebook;

[ReactiveComponent]
public static partial class NotebookCellsComponent
{
    public static ReactiveNode Render(in NotebookCellsProps props, ref Hooks hooks)
        => Reactive.ForEach(RenderCell, BuildCells(props));

    private static (string Key, CellItem Value)[] BuildCells(
        scoped in NotebookCellsProps props)
    {
        var items = new (string Key, CellItem Value)[props.Cells.Length];
        for (var index = 0; index < props.Cells.Length; index++) {
            items[index] = (
                props.Cells[index].Id,
                new(props.View, props.Cells[index]));
        }
        return items;
    }

    private static ReactiveNode<EffectTerm<RenderEffect<NotebookCellSnapshot>>>
        RenderCell(scoped in CellItem item)
        => new(Term.Effect(new RenderEffect<NotebookCellSnapshot>(item.View, item.Value)));

    private readonly record struct CellItem(
        INotebookView View,
        NotebookCellSnapshot Value);
}
