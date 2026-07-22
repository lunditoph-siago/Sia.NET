namespace Sia.Reactive;

[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class ReactiveComponentAttribute : Attribute;

public delegate ReactiveNode ReactiveComponent<TProps>(
    in TProps props,
    ref Hooks hooks)
    where TProps : struct;

internal readonly record struct ComponentSpec<TProps>(
    ReactiveComponent<TProps> Render,
    TProps Props)
    : ISpec<ComponentSpec<TProps>, Unit, OpaqueTerm>
    where TProps : struct
{
    public static OpaqueTerm Expand(
        in ComponentSpec<TProps> props,
        in Unit state,
        in ExpandContext context)
    {
        var hooks = new Hooks(context);
        var tree = props.Render(props.Props, ref hooks);
        hooks.CompleteRender();
        return new(tree);
    }
}

public readonly record struct ComponentTerm<TProps>(
    ReactiveComponent<TProps> Render,
    TProps Props)
    : ITerm<ComponentTerm<TProps>>
    where TProps : struct
{
    public static int SlotCount => 1;

    public static void Mount(
        in ComponentTerm<TProps> self,
        ref GraphContext context)
        => context.SetSlot(context.Reconciler.MountSub(
            new ComponentSpec<TProps>(self.Render, self.Props),
            context.Cell,
            context.Depth + 1,
            context.NextSlotIndex,
            context.Schedule,
            context.Scope,
            context.Output));

    public static void Reconcile(
        in ComponentTerm<TProps> previous,
        in ComponentTerm<TProps> next,
        ref GraphContext context)
    {
        var child = context.PeekSlot();
        if (child is not { IsValid: true }
            || !child.ContainsUnchecked<ComponentSpec<TProps>>()) {
            Mount(next, ref context);
            return;
        }

        ref var current = ref child.GetUnchecked<ComponentSpec<TProps>>();
        if (!EqualityComparer<ReactiveComponent<TProps>>.Default.Equals(
                current.Render, next.Render)) {
            context.RemountRange(1);
            Mount(next, ref context);
            return;
        }
        if (!EqualityComparer<TProps>.Default.Equals(
                current.Props, next.Props)) {
            context.Reconciler.UpdateMount(
                child,
                new ComponentSpec<TProps>(next.Render, next.Props));
        }
        context.Advance();
    }
}

public readonly record struct ContextRenderSpec<TContext, TTerm>(
    ReactiveContextRenderer<TContext, TTerm> Render)
    : ISpec<ContextRenderSpec<TContext, TTerm>, Unit, TTerm>
    where TContext : struct
    where TTerm : struct, ITerm<TTerm>
{
    public static TTerm Expand(
        in ContextRenderSpec<TContext, TTerm> props,
        in Unit state,
        in ExpandContext context)
    {
        var value = context.Use<TContext>();
        return props.Render(value).Term;
    }
}
