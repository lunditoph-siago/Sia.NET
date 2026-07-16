using Sia;
using Sia.Reactive;

using ExampleTree = Sia.Reactive.EffectTerm<
    Sia_Examples.RenderEffect<Sia_Examples.ExampleItemView>>;
using AppTree = Sia.Reactive.Group<
    Sia.Reactive.ForEachTerm<int, Sia_Examples.ExampleItem>,
    Sia.Reactive.EffectTerm<
        Sia_Examples.RenderEffect<Sia_Examples.ExampleOutputView>>,
    Sia.Reactive.EffectTerm<Sia_Examples.ExampleCommitEffect>>;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    private static readonly ExampleRunner _runner = new();

    public static void Dispose() => _runner.Dispose();
}

internal sealed class ExampleController
{
    private State<ExampleAppState> _state;

    public void Attach(State<ExampleAppState> state)
        => _state = state;

    public void BeginRun(int index, string title)
        => _state.Set(new(index, title, "Running\u2026", true));

    public void CompleteRun(string output)
    {
        var current = _state.Value;
        _state.Set(current with { Output = output, Loading = false });
    }
}

internal readonly record struct ExampleAppState(
    int SelectedIndex,
    string Title,
    string Output,
    bool Loading)
{
    public static ExampleAppState Initial { get; }
        = new(-1, "Select an example", "\u2190 Choose an example to run it", false);
}

internal readonly record struct ExampleApp(
    ExampleRunner Runner,
    ExampleController Controller,
    IExampleRenderHost Host)
    : ISpec<ExampleApp, Unit, AppTree>
{
    public static AppTree Expand(
        in ExampleApp props,
        in Unit state,
        in ExpandContext context)
    {
        var view = context.UseState(ExampleAppState.Initial);
        props.Controller.Attach(view);

        var examples = props.Runner.Examples;
        var children = new Keyed<int, ExampleItem>[examples.Count];
        for (var index = 0; index < examples.Count; index++) {
            var example = examples[index];
            children[index] = Term.Keyed(
                index,
                new ExampleItem(
                    props.Host,
                    index,
                    example.Name,
                    example.Description,
                    view.Value.SelectedIndex == index));
        }

        return Term.Group(
            Term.ForEach<int, ExampleItem>(children),
            Term.Effect(new RenderEffect<ExampleOutputView>(
                props.Host,
                new(
                    view.Value.Title,
                    view.Value.Output,
                    view.Value.Loading))),
            Term.Effect(new ExampleCommitEffect(props.Host)));
    }
}

internal readonly record struct ExampleItem(
    IExampleRenderHost Host,
    int Index,
    string Name,
    string Description,
    bool Active)
    : ISpec<ExampleItem, Unit, ExampleTree>
{
    public static ExampleTree Expand(
        in ExampleItem props,
        in Unit state,
        in ExpandContext context)
        => Term.Effect(new RenderEffect<ExampleItemView>(
            props.Host,
            new(
                props.Index,
                props.Name,
                props.Description,
                props.Active)));
}

internal readonly record struct ExampleItemView(
    int Index,
    string Name,
    string Description,
    bool Active);

internal readonly record struct ExampleOutputView(
    string Title,
    string Output,
    bool Loading);

internal interface IRenderHost<TView>
    where TView : struct, IEquatable<TView>
{
    void Upsert(in TView view);
    void Remove(in TView view);
}

internal interface IExampleRenderHost
    : IRenderHost<ExampleItemView>, IRenderHost<ExampleOutputView>
{
    void Commit();
}

internal readonly record struct RenderEffect<TView>(
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

internal readonly record struct ExampleCommitEffect(IExampleRenderHost Host)
    : IEffect<ExampleCommitEffect>
{
    public static void Mount(in ExampleCommitEffect self) => self.Host.Commit();

    public static void Reconcile(
        in ExampleCommitEffect previous,
        in ExampleCommitEffect next)
        => next.Host.Commit();

    public static void Unmount(in ExampleCommitEffect self) { }
}
