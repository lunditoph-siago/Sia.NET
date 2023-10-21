namespace Sia;

// SID: Sia Identifier
public record struct Sid<TId>(TId Value)
    where TId : notnull
{
    public TId Previous { get; private set; }

    public readonly record struct SetValue(TId Value) : IParallelCommand, IReconstructableCommand<SetValue>
    {
        public static SetValue ReconstructFromCurrentState(in EntityRef entity)
            => new(entity.Get<Sid<TId>>().Value);

        public void Execute(World world, in EntityRef target)
            => ExecuteOnParallel(target);

        public void ExecuteOnParallel(in EntityRef target)
        {
            ref var id = ref target.Get<Sid<TId>>();
            id.Previous = id.Value;
            id.Value = Value;
        }
    }
}
public static class Sid
{
    public static Sid<TId> From<TId>(in TId id)
        where TId : notnull
        => new(id);
}