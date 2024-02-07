namespace Sia;

using System.Runtime.CompilerServices;

public static class EntityWorldContextExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Send<TEvent>(this EntityRef entity, in TEvent e)
        where TEvent : IEvent
        => Context<World>.Current!.Send(entity, e);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Send<TEntity, TEvent>(this EntityRef<TEntity> entity, in TEvent e)
        where TEntity : struct
        where TEvent : IEvent
        => Context<World>.Current!.Send(entity, e);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Modify<TCommand>(this EntityRef entity, in TCommand command)
        where TCommand : ICommand
        => Context<World>.Current!.Modify(entity, command);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Modify<TEntity, TCommand>(this EntityRef<TEntity> entity, in TCommand command)
        where TEntity : struct
        where TCommand : ICommand
        => Context<World>.Current!.Modify(entity, command);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Modify<TEntity, TComponent, TCommand>(
        this EntityRef<TEntity> entity, ref TComponent component, in TCommand command)
        where TEntity : struct
        where TCommand : ICommand<TComponent>
        => Context<World>.Current!.Modify(entity, ref component, command);
}