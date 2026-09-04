using System.Runtime.CompilerServices;

namespace Sia.Reactive;

internal static class ContextLookup
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TContext Get<TContext>(Reconciler reconciler, Entity cell)
        where TContext : struct
    {
        if (TryGet(reconciler, cell, out TContext value)) {
            return value;
        }
        throw new InvalidOperationException(
            $"No provider found for context type {typeof(TContext)}.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TContext GetOrDefault<TContext>(
        Reconciler reconciler,
        Entity cell,
        scoped in TContext fallback)
        where TContext : struct
        => TryGet(reconciler, cell, out TContext value) ? value : fallback;

    private static bool TryGet<TContext>(
        Reconciler reconciler,
        Entity cell,
        out TContext value)
        where TContext : struct
    {
        for (var scope = cell.GetUnchecked<Cell>().Scope; scope != null; scope = scope.Parent) {
            if (scope.ContextType != typeof(TContext)) {
                continue;
            }
            ref var node = ref scope.ProviderSlot.GetUnchecked<ContextNode<TContext>>();
            reconciler.RecordContextDependency(cell, scope);
            value = node.Value;
            return true;
        }
        value = default;
        return false;
    }
}
