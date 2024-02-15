using System.Diagnostics.CodeAnalysis;

namespace Sia;

public static class EntityUtility
{
    public static void CheckComponent<TComponent>(EntityRef entity)
        => CheckComponent<TComponent>(entity.Descriptor);

    public static void CheckComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TComponent>()
        => CheckComponent<TComponent>(EntityDescriptor.Get<TEntity>());

    public static void CheckComponent<TComponent>(EntityDescriptor descriptor)
    {
        if (!descriptor.Contains<TComponent>()) {
            throw new InvalidDataException(
                "Entity does not contain required component " + typeof(TComponent));
        }
    }
}