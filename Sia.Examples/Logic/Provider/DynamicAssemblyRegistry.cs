using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace Sia_Examples.Notebook;

internal static class DynamicAssemblyRegistry
{
    private static readonly ConcurrentDictionary<string, byte[]> _images =
        new(StringComparer.OrdinalIgnoreCase);

    private static int _hooked;

    public static void Register(string name, byte[] image)
    {
        _images.TryAdd(name, image);
        EnsureHooked();
    }

    private static void EnsureHooked()
    {
        if (Interlocked.Exchange(ref _hooked, 1) != 0) {
            return;
        }
        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
            assemblyName.Name is { } name && _images.TryGetValue(name, out var image)
                ? Assembly.Load(image)
                : null;
    }
}
