using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace Sia_Examples.Notebook;

internal static class DynamicAssemblyRegistry
{
    private static readonly ConcurrentDictionary<string, Assembly> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte[]> _images =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte[]> _analyzers =
        new(StringComparer.OrdinalIgnoreCase);

    private static int _hooked;
    private static int _version;

    public static int Version => Volatile.Read(ref _version);

    public static IEnumerable<string> AnalyzerNames => _analyzers.Keys;

    public static void Register(string name, Assembly assembly)
    {
        if (!_loaded.TryAdd(name, assembly)) {
            return;
        }
        Interlocked.Increment(ref _version);
        EnsureHooked();
    }

    public static void Register(string name, byte[] image)
    {
        if (!_images.TryAdd(name, image)) {
            return;
        }
        Interlocked.Increment(ref _version);
        EnsureHooked();
    }

    public static void RegisterAnalyzer(string name, byte[] image)
    {
        if (!_analyzers.TryAdd(name, image)) {
            return;
        }
        Interlocked.Increment(ref _version);
        EnsureHooked();
    }

    private static void EnsureHooked()
    {
        if (Interlocked.Exchange(ref _hooked, 1) != 0) {
            return;
        }
        AssemblyLoadContext.Default.Resolving += (_, assemblyName) => {
            if (assemblyName.Name is not { } name) {
                return null;
            }
            if (_loaded.TryGetValue(name, out var loaded)) {
                return loaded;
            }
            if (_images.TryGetValue(name, out var image)
                || _analyzers.TryGetValue(name, out image)) {
                return Assembly.Load(image);
            }
            return null;
        };
    }
}
