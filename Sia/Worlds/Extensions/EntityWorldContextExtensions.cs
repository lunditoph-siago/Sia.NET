namespace Sia;

using System.Runtime.CompilerServices;

public static class EntityWorldContextExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Send<TEvent>(this Entity entity, in TEvent e)
        where TEvent : IEvent
        => World.Current.Send(entity, e);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Execute<TCommand>(this Entity entity, in TCommand command)
        where TCommand : ICommand
        => World.Current.Execute(entity, command);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Execute<TComponent, TCommand>(
        this Entity entity, ref TComponent component, in TCommand command)
        where TCommand : ICommand<TComponent>
        => World.Current.Execute(entity, ref component, command);
}