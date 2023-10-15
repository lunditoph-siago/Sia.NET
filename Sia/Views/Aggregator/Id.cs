namespace Sia;

public record struct Id<TId>(TId Value)
    where TId : IEquatable<TId>
{
    public TId Previous { get; private set; }

    public readonly record struct SetValue(TId Value) : IParallelCommand, IReconstructableCommand<SetValue>
    {
        public static SetValue ReconstructFromCurrentState(in EntityRef entity)
            => new(entity.Get<Id<TId>>().Value);

        public void Execute(World world, in EntityRef target)
            => ExecuteOnParallel(target);

        public void ExecuteOnParallel(in EntityRef target)
        {
            ref var id = ref target.Get<Id<TId>>();
            id.Previous = id.Value;
            id.Value = Value;
        }
    }
}