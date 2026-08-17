namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct WorldEntity(World World, Entity Entity) : IWorldEntity
{
    public EntityId Id => Entity.Id;
    public bool IsValid => Entity.IsValid;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>()
        => ref Entity.Get<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntity Add<TComponent>(in TComponent initial)
    {
        Entity.Add(initial);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntity Set<TComponent>(in TComponent value)
    {
        Entity.Set(value);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntity Remove<TComponent>(out bool success)
    {
        Entity.Remove<TComponent>(out success);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Destroy()
        => Entity.Destroy();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send<TEvent>(in TEvent e)
        where TEvent : IEvent
        => World.Send(Entity, e);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute<TCommand>(in TCommand command)
        where TCommand : ICommand
        => World.Execute(Entity, command);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute<TComponent, TCommand>(
        ref TComponent component, in TCommand command)
        where TCommand : ICommand<TComponent>
        => World.Execute(Entity, ref component, command);
}
