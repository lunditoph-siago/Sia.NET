namespace Sia;

public interface IEntityAccessor
{
    bool Contains<TComponent>(long pointer);
    bool Contains(long pointer, Type type);

    ref TComponent Get<TComponent>(long pointer);
    ref TComponent GetOrNullRef<TComponent>(long pointer);
}