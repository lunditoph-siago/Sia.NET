namespace Sia;

public class CallbackSystem<TWorld> : SystemBase<TWorld>
    where TWorld : World<EntityRef>
{
    private Action<TWorld, Scheduler, EntityRef> _execute;
    private Action<TWorld, Scheduler>? _beforeExecute;
    private Action<TWorld, Scheduler>? _afterExecute;

    public CallbackSystem(
        Action<TWorld, Scheduler, EntityRef> execute,
        Action<TWorld, Scheduler>? beforeExecute = null,
        Action<TWorld, Scheduler>? afterExecute = null)
    {
        _execute = execute;
        _beforeExecute = beforeExecute;
        _afterExecute = afterExecute;
    }

    public override void Execute(TWorld world, Scheduler scheduler, EntityRef entity)
        => _execute(world, scheduler, entity);

    public override void BeforeExecute(TWorld world, Scheduler scheduler)
        => _beforeExecute?.Invoke(world, scheduler);

    public override void AfterExecute(TWorld world, Scheduler scheduler)
        => _afterExecute?.Invoke(world, scheduler);
}