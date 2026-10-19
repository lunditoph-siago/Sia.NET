using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace Sia.Examples.Notebook;

internal static class DynamicAssemblyRegistry
{
    private static readonly ConcurrentDictionary<string, Assembly> s_Loaded =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte[]> s_Images =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte[]> s_Analyzers =
        new(StringComparer.OrdinalIgnoreCase);

    private static int s_Hooked;
    private static int s_Version;

    public static int Version => Volatile.Read(ref s_Version);

    public static IEnumerable<string> AnalyzerNames => s_Analyzers.Keys;

    public static void Register(string name, Assembly assembly)
    {
        if (!s_Loaded.TryAdd(name, assembly)) {
            return;
        }
        Interlocked.Increment(ref s_Version);
        EnsureHooked();
    }

    public static void Register(string name, byte[] image)
    {
        if (!s_Images.TryAdd(name, image)) {
            return;
        }
        Interlocked.Increment(ref s_Version);
        EnsureHooked();
    }

    public static void RegisterAnalyzer(string name, byte[] image)
    {
        if (!s_Analyzers.TryAdd(name, image)) {
            return;
        }
        Interlocked.Increment(ref s_Version);
        EnsureHooked();
    }

    private static void EnsureHooked()
    {
        if (Interlocked.Exchange(ref s_Hooked, 1) != 0) {
            return;
        }
        AssemblyLoadContext.Default.Resolving += (_, assemblyName) => {
            if (assemblyName.Name is not { } name) {
                return null;
            }
            if (s_Loaded.TryGetValue(name, out var loaded)) {
                return loaded;
            }
            if (s_Images.TryGetValue(name, out var image)
                || s_Analyzers.TryGetValue(name, out image)) {
                return Assembly.Load(image);
            }
            return null;
        };
    }
}
