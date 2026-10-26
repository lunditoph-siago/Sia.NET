using Sia.Reactive;

namespace Sia.Examples.Notebook;

[ReactiveComponent]
public static partial class NotebookPackagesComponent
{
    public static ReactiveNode Render(in NotebookPackagesProps props, ref Hooks hooks)
        => new ReactiveNode<ForEachTerm<PackageKey, PackageItem>>(new(BuildPackages(props)));

    private static Keyed<PackageKey, PackageItem>[] BuildPackages(
        scoped in NotebookPackagesProps props)
    {
        var items = new Keyed<PackageKey, PackageItem>[props.Packages.Length];
        for (var index = 0; index < props.Packages.Length; index++) {
            var status = props.Packages[index];
            items[index] = new(
                new(status.Package.Id, status.Package.Version),
                new(props.View, new(index, status)));
        }
        return items;
    }

    private readonly record struct PackageItem(
        INotebookView View,
        PackageView Value)
        : ISpec<PackageItem, Unit, EffectTerm<RenderEffect<PackageView>>>
    {
        public static EffectTerm<RenderEffect<PackageView>> Expand(
            in PackageItem item, in Unit state, in ExpandContext context)
            => Term.Effect(new RenderEffect<PackageView>(item.View, item.Value));
    }

    private readonly record struct PackageKey(
        string Id,
        string? Version);
}
