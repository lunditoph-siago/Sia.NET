using Sia.Reactive;

namespace Sia_Examples.Notebook;

[ReactiveComponent]
public static partial class NotebookViewComponent
{
    public static ReactiveNode Render(in NotebookViewProps props, ref Hooks hooks)
    {
        var dockState = hooks.UseState(props.InitialDockState);
        return Reactive.Group(
            Reactive.Component<NotebookCellsProps>(
                NotebookCellsComponent.Render,
                new(props.View, props.Snapshot.Cells)),
            Reactive.Component<NotebookPackagesProps>(
                NotebookPackagesComponent.Render,
                new(props.View, props.Snapshot.Packages)),
            RenderPackageCount(new(props.View, new(props.Snapshot.Packages.Length))),
            RenderDock(new(props.View, new(dockState.Value))));
    }

    private static ReactiveNode<EffectTerm<RenderEffect<PackageCountView>>> RenderPackageCount(
        scoped in PackageCountItem item)
        => new(Term.Effect(new RenderEffect<PackageCountView>(item.View, item.Value)));

    private static ReactiveNode<EffectTerm<RenderEffect<NotebookDockPresentation>>> RenderDock(
        scoped in DockItem item)
        => new(Term.Effect(new RenderEffect<NotebookDockPresentation>(item.View, item.Value)));

    private readonly record struct PackageCountItem(
        INotebookView View,
        PackageCountView Value);

    private readonly record struct DockItem(
        INotebookView View,
        NotebookDockPresentation Value);
}
