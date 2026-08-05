using Sia;
using Sia.Reactive;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    private static readonly Notebook.NotebookLibrary _library = new();
}

public readonly record struct ExampleAppProps(
    Notebook.NotebookLibrary Library,
    IExampleRenderHost Host);

public readonly record struct ExampleAppState(int SelectedIndex)
{
    public static ExampleAppState Initial { get; } = new(-1);
}

[ReactiveComponent]
public static partial class ExampleApp
{
    public static ReactiveNode Render(
        in ExampleAppProps props,
        ref Hooks hooks)
    {
        var state = hooks.UseState(ExampleAppState.Initial);

        hooks.UseEffect(
            new CommitDeps(props.Host, state.Value),
            static (in CommitDeps d) => { d.Host.Commit(); return default(Unit); },
            static (in Unit _) => { });

        return Reactive.ForEach(RenderItem, BuildItems(props, state.Value.SelectedIndex));
    }

    private static (int Key, ExampleItem Value)[] BuildItems(
        scoped in ExampleAppProps props,
        int selectedIndex)
    {
        var notebooks = props.Library.Notebooks;
        var items = new (int Key, ExampleItem Value)[notebooks.Count];
        for (var index = 0; index < notebooks.Count; index++) {
            var notebook = notebooks[index];
            items[index] = (
                index,
                new(
                    props.Host,
                    index,
                    notebook.Name,
                    notebook.Description,
                    selectedIndex == index));
        }
        return items;
    }

    private static ReactiveNode<EffectTerm<RenderEffect<ExampleItemView>>>
        RenderItem(scoped in ExampleItem item)
        => new(Term.Effect(new RenderEffect<ExampleItemView>(
            item.Host,
            new(item.Index, item.Name, item.Description, item.Active))));

    private readonly record struct CommitDeps(
        IExampleRenderHost Host, ExampleAppState State);
}

public readonly record struct ExampleItem(
    IExampleRenderHost Host,
    int Index,
    string Name,
    string Description,
    bool Active);

public readonly record struct ExampleItemView(
    int Index,
    string Name,
    string Description,
    bool Active);

public interface IRenderHost<TView>
    where TView : struct, IEquatable<TView>
{
    void Upsert(in TView view);
    void Remove(in TView view);
}

public interface IExampleRenderHost
    : IRenderHost<ExampleItemView>
{
    void Commit();
}

public readonly record struct RenderEffect<TView>(
    IRenderHost<TView> Host,
    TView View)
    : IEffect<RenderEffect<TView>>
    where TView : struct, IEquatable<TView>
{
    public static void Mount(in RenderEffect<TView> self)
        => self.Host.Upsert(self.View);

    public static void Reconcile(
        in RenderEffect<TView> previous,
        in RenderEffect<TView> next)
    {
        if (!ReferenceEquals(previous.Host, next.Host)) {
            previous.Host.Remove(previous.View);
            next.Host.Upsert(next.View);
        }
        else if (!previous.View.Equals(next.View)) {
            next.Host.Upsert(next.View);
        }
    }

    public static void Unmount(in RenderEffect<TView> self)
        => self.Host.Remove(self.View);
}
