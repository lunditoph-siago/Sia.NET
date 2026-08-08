using Sia.Reactive;
using Sia_Examples.Notebook;

namespace Sia_Examples;

[ReactiveComponent]
public static partial class ExampleApp
{
    public static ReactiveNode Render(in ExampleAppProps props, ref Hooks hooks)
    {
        var state = hooks.UseState(ExampleAppState.Initial);
        return Reactive.ForEach(
            RenderItem,
            BuildItems(props, state.Value.SelectedIndex));
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
                    new(
                        index,
                        notebook.Name,
                        notebook.Description,
                        selectedIndex == index)));
        }
        return items;
    }

    private static ReactiveNode<EffectTerm<RenderEffect<ExampleItemView>>> RenderItem(
        scoped in ExampleItem item)
        => new(Term.Effect(new RenderEffect<ExampleItemView>(item.Host, item.View)));

    private readonly record struct ExampleItem(
        IRenderHost<ExampleItemView> Host,
        ExampleItemView View);
}
