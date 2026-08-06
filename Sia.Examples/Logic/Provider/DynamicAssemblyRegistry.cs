using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace Sia_Examples.Notebook;

internal static class DynamicAssemblyRegistry
{
    private static readonly ConcurrentDictionary<string, byte[]> ByName = new(StringComparer.OrdinalIgnoreCase);
    private static int _hooked;

    public static void Register(string name, byte[] image)
    {
        ByName[name] = image;
        EnsureHooked();
    }

    private static void EnsureHooked()
    {
        if (Interlocked.Exchange(ref _hooked, 1) != 0) return;
        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
            assemblyName.Name is { } name && ByName.TryGetValue(name, out var image)
                ? Assembly.Load(image)
                : null;
    }
}
