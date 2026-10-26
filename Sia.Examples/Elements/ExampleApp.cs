using Sia.Reactive;
using Sia.Examples.Notebook;

namespace Sia.Examples;

[ReactiveComponent]
public static partial class ExampleApp
{
    public static ReactiveNode Render(in ExampleAppProps props, ref Hooks hooks)
    {
        var state = hooks.UseState(ExampleAppState.Initial);
        return new ReactiveNode<ForEachTerm<int, ExampleItem>>(
            new(BuildItems(props, state.Value.SelectedIndex)));
    }

    private static Keyed<int, ExampleItem>[] BuildItems(
        scoped in ExampleAppProps props,
        int selectedIndex)
    {
        var notebooks = props.Library.Notebooks;
        var items = new Keyed<int, ExampleItem>[notebooks.Count];
        for (var index = 0; index < notebooks.Count; index++) {
            var notebook = notebooks[index];
            items[index] = new(
                index,
                new(
                    props.Host,
                    new(
                        index,
                        notebook.Name,
                        notebook.Description,
                        notebook.Origin,
                        selectedIndex == index)));
        }
        return items;
    }

    private readonly record struct ExampleItem(
        IRenderHost<ExampleItemView> Host,
        ExampleItemView View)
        : ISpec<ExampleItem, Unit, EffectTerm<RenderEffect<ExampleItemView>>>
    {
        public static EffectTerm<RenderEffect<ExampleItemView>> Expand(
            in ExampleItem item, in Unit state, in ExpandContext context)
            => Term.Effect(new RenderEffect<ExampleItemView>(item.Host, item.View));
    }
}
