namespace Sia;

public record struct Id<TId>(TId Value)
    where TId : IEquatable<TId>
{
    public TId Previous { get; private set; }

    public readonly record struct SetValue(TId Value) : IParallelCommand
    {
        public void Execute(World world, in EntityRef target)
            => ExecuteOnParallel(target);

        public void ExecuteOnParallel(in EntityRef target)
        {
            ref var TId = ref target.Get<Id<TId>>();
            TId.Previous = TId.Value;
            TId.Value = Value;
        }
    }
}