namespace Sia;

public sealed class EntityBuilder
{
    private interface IDynBundle
    {
        object? Boxed { get; }

        EntityRef Create(IEntityCreator creator);
        IDynBundle Add<TComponent>(in TComponent initial);
    }

    private class DynBundle<TBundle>(in TBundle value) : IDynBundle
        where TBundle : struct
    {
        public object? Boxed => _value;
        private readonly TBundle _value = value;

        public EntityRef Create(IEntityCreator creator)
            => creator.CreateEntity(_value);
        
        public IDynBundle Add<TComponent>(in TComponent initial)
        {
            var bundle = Bundle.Create(initial, _value);
            return new DynBundle<Bundle<TComponent, TBundle>>(bundle);
        }
    }

    public object? Boxed => _dynBundle?.Boxed;

    private IDynBundle? _dynBundle = null;

    public void Add<TComponent>(in TComponent initial)
    {
        _dynBundle = _dynBundle == null
            ? new DynBundle<Bundle<TComponent>>(Bundle.Create(initial))
            : _dynBundle.Add(initial);
    }

    public EntityRef Create(IEntityCreator creator)
        => (_dynBundle ?? throw new InvalidOperationException("Empty entity")).Create(creator);
}