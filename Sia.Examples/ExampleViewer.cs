using Sia;
using Sia.Reactive;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    private static readonly ExampleRunner _runner = new();

    public static void Dispose() => _runner.Dispose();
}

public readonly record struct ExampleAppProps(
    ExampleRunner Runner,
    IExampleRenderHost Host);

public readonly record struct ExampleAppState(
    int SelectedIndex,
    string Title,
    string Output,
    bool Loading)
{
    public static ExampleAppState Initial { get; }
        = new(-1, "Select an example", "\u2190 Choose an example to run it", false);
}

public abstract record ExampleAppMessage;

public sealed record BeginExampleRun(int Index, string Title)
    : ExampleAppMessage;

public sealed record CompleteExampleRun(string Output)
    : ExampleAppMessage;

[ReactiveComponent]
public static partial class ExampleApp
{
    extension(scoped in ExampleAppProps props)
    {
        public ExampleAppState InitialState => ExampleAppState.Initial;

        public ReactiveNode Render(scoped in ExampleAppState state)
        {
            var items = Reactive.ForEach(
                RenderItem,
                BuildItems(props, state.SelectedIndex));

            return Reactive.Group(
                items,
                Effect(new RenderEffect<ExampleOutputView>(
                    props.Host,
                    new(state.Title, state.Output, state.Loading))),
                Effect(new ExampleCommitEffect(props.Host)));
        }
    }

    extension(scoped in ExampleAppState state)
    {
        public ExampleAppState Reduce(scoped in ExampleAppMessage message)
            => message switch {
                BeginExampleRun begin => new(
                    begin.Index,
                    begin.Title,
                    "Running\u2026",
                    true),
                CompleteExampleRun complete => state with {
                    Output = complete.Output,
                    Loading = false,
                },
                _ => state,
            };
    }

    private static (int Key, ExampleItem Value)[] BuildItems(
        scoped in ExampleAppProps props,
        int selectedIndex)
    {
        var examples = props.Runner.Examples;
        var items = new (int Key, ExampleItem Value)[examples.Count];
        for (var index = 0; index < examples.Count; index++) {
            var example = examples[index];
            items[index] = (
                index,
                new(
                    props.Host,
                    index,
                    example.Name,
                    example.Description,
                    selectedIndex == index));
        }
        return items;
    }

    private static ReactiveNode<EffectTerm<RenderEffect<ExampleItemView>>>
        RenderItem(scoped in ExampleItem item)
        => Effect(new RenderEffect<ExampleItemView>(
            item.Host,
            new(item.Index, item.Name, item.Description, item.Active)));

    private static ReactiveNode<EffectTerm<TEffect>> Effect<TEffect>(
        scoped in TEffect effect)
        where TEffect : struct, IEffect<TEffect>
        => new(Term.Effect(effect));
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

public readonly record struct ExampleOutputView(
    string Title,
    string Output,
    bool Loading);

public interface IRenderHost<TView>
    where TView : struct, IEquatable<TView>
{
    void Upsert(in TView view);
    void Remove(in TView view);
}

public interface IExampleRenderHost
    : IRenderHost<ExampleItemView>, IRenderHost<ExampleOutputView>
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

public readonly record struct ExampleCommitEffect(IExampleRenderHost Host)
    : IEffect<ExampleCommitEffect>
{
    public static void Mount(in ExampleCommitEffect self) => self.Host.Commit();

    public static void Reconcile(
        in ExampleCommitEffect previous,
        in ExampleCommitEffect next)
        => next.Host.Commit();

    public static void Unmount(in ExampleCommitEffect self) { }
}
