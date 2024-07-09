namespace Sia;

public static class EntityUtility
{
    public static void CheckComponent<TComponent>(Entity entity)
        => CheckComponent<TComponent>(entity.Descriptor);

    public static void CheckComponent<TEntity, TComponent>()
        where TEntity : struct, IHList
        => CheckComponent<TComponent>(EntityDescriptor.Get<TEntity>());

    public static void CheckComponent<TComponent>(EntityDescriptor descriptor)
    {
        if (descriptor.GetOffset<TComponent>() == -1) {
            throw new InvalidDataException(
                "Entity does not contain required component " + typeof(TComponent));
        }
    }
}