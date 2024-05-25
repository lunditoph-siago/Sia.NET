namespace Sia;

using System.Diagnostics.CodeAnalysis;

public static class EntityExceptionHelper
{
    [DoesNotReturn]
    public static void ThrowComponentExisted<T>()
        => throw new ComponentConflictException("Component has already existed: " + typeof(T));

    [DoesNotReturn]
    public static void ThrowComponentNotExisted<T>()
        => throw new ComponentConflictException("Component does not exist: " + typeof(T));
}