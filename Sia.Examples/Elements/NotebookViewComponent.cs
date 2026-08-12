using Sia.Reactive;

namespace Sia_Examples.Notebook;

[ReactiveComponent]
public static partial class NotebookViewComponent
{
    public static ReactiveNode Render(in NotebookViewProps props, ref Hooks hooks)
    {
        var cellState = hooks.UseState(props.InitialCellState);
        return Reactive.Group(
            Reactive.Component<NotebookCellsProps>(
                NotebookCellsComponent.Render,
                new(props.View, props.Snapshot.Cells)),
            Reactive.Component<NotebookPackagesProps>(
                NotebookPackagesComponent.Render,
                new(props.View, props.Snapshot.Packages)),
            RenderPackageCount(new(props.View, new(props.Snapshot.Packages.Length))),
            RenderCell(new(props.View, new(cellState.Value))));
    }

    private static ReactiveNode<EffectTerm<RenderEffect<PackageCountView>>> RenderPackageCount(
        scoped in PackageCountItem item)
        => new(Term.Effect(new RenderEffect<PackageCountView>(item.View, item.Value)));

    private static ReactiveNode<EffectTerm<RenderEffect<NotebookCellPresentation>>> RenderCell(
        scoped in CellItem item)
        => new(Term.Effect(new RenderEffect<NotebookCellPresentation>(item.View, item.Value)));

    private readonly record struct PackageCountItem(
        INotebookView View,
        PackageCountView Value);

    private readonly record struct CellItem(
        INotebookView View,
        NotebookCellPresentation Value);
}
