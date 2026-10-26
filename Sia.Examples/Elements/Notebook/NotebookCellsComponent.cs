using Sia.Reactive;

namespace Sia.Examples.Notebook;

[ReactiveComponent]
public static partial class NotebookCellsComponent
{
    public static ReactiveNode Render(in NotebookCellsProps props, ref Hooks hooks)
        => new ReactiveNode<ForEachTerm<string, CellItem>>(new(BuildCells(props)));

    private static Keyed<string, CellItem>[] BuildCells(
        scoped in NotebookCellsProps props)
    {
        var items = new Keyed<string, CellItem>[props.Cells.Length];
        for (var index = 0; index < props.Cells.Length; index++) {
            var cell = props.Cells[index];
            items[index] = new(cell.Id, new(props.View, cell));
        }
        return items;
    }

    private readonly record struct CellItem(
        INotebookView View,
        NotebookCellSnapshot Value)
        : ISpec<CellItem, Unit, EffectTerm<RenderEffect<NotebookCellSnapshot>>>
    {
        public static EffectTerm<RenderEffect<NotebookCellSnapshot>> Expand(
            in CellItem item, in Unit state, in ExpandContext context)
            => Term.Effect(new RenderEffect<NotebookCellSnapshot>(item.View, item.Value));
    }
}
