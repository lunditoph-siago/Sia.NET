namespace Sia;

using System.Runtime.CompilerServices;

internal static class EntityWorldOwner
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static World? TryGet(Entity entity)
        => entity.Host?.World;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static World Require(Entity entity)
        => TryGet(entity)
            ?? throw new InvalidOperationException(
                "The entity is not attached to a world.");
}
