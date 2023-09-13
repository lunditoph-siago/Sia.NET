namespace Sia;

public sealed class CallbackSystem<TWorld> : SystemBase<TWorld>
    where TWorld : World<EntityRef>
{
    public delegate void ExecuteListener(TWorld world, Scheduler scheduler, in EntityRef entity);

    private readonly ExecuteListener _execute;
    private readonly Action<TWorld, Scheduler>? _beforeExecute;
    private readonly Action<TWorld, Scheduler>? _afterExecute;

    public CallbackSystem(
        ExecuteListener execute,
        Action<TWorld, Scheduler>? beforeExecute = null,
        Action<TWorld, Scheduler>? afterExecute = null)
    {
        _execute = execute;
        _beforeExecute = beforeExecute;
        _afterExecute = afterExecute;
    }

    public override void Execute(TWorld world, Scheduler scheduler, in EntityRef entity)
        => _execute(world, scheduler, entity);

    public override void BeforeExecute(TWorld world, Scheduler scheduler)
        => _beforeExecute?.Invoke(world, scheduler);

    public override void AfterExecute(TWorld world, Scheduler scheduler)
        => _afterExecute?.Invoke(world, scheduler);
}