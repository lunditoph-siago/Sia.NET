using Sia.Reactive;

namespace Sia_Examples.Notebook;

[ReactiveComponent]
public static partial class NotebookPackagesComponent
{
    public static ReactiveNode Render(in NotebookPackagesProps props, ref Hooks hooks)
        => Reactive.ForEach(RenderPackage, BuildPackages(props));

    private static (PackageKey Key, PackageItem Value)[] BuildPackages(
        scoped in NotebookPackagesProps props)
    {
        var items = new (PackageKey Key, PackageItem Value)[props.Packages.Length];
        for (var index = 0; index < props.Packages.Length; index++) {
            var status = props.Packages[index];
            items[index] = (
                new(status.Package.Source, status.Package.Id, status.Package.Version),
                new(props.View, new(index, status)));
        }
        return items;
    }

    private static ReactiveNode<EffectTerm<RenderEffect<PackageView>>> RenderPackage(
        scoped in PackageItem item)
        => new(Term.Effect(new RenderEffect<PackageView>(item.View, item.Value)));

    private readonly record struct PackageItem(
        INotebookView View,
        PackageView Value);

    private readonly record struct PackageKey(
        PackageSource Source,
        string Id,
        string? Version);
}
