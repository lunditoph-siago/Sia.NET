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
    private static readonly object s_Gate = new();

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

    internal static void RegisterImages(IReadOnlyList<FetchedAssembly> assemblies)
    {
        lock (s_Gate) {
            var candidates = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in assemblies) {
                if (candidates.TryGetValue(assembly.Name, out var candidate) && !candidate.AsSpan().SequenceEqual(assembly.Image)
                    || s_Images.TryGetValue(assembly.Name, out candidate) && !candidate.AsSpan().SequenceEqual(assembly.Image)
                    || s_Analyzers.TryGetValue(assembly.Name, out candidate) && !candidate.AsSpan().SequenceEqual(assembly.Image))
                    throw new InvalidOperationException($"Assembly '{assembly.Name}' is already registered with different contents. Reload before changing package versions.");
                candidates[assembly.Name] = assembly.Image;
            }
            var changed = false;
            foreach (var assembly in assemblies)
                changed |= (assembly.Analyzer ? s_Analyzers : s_Images).TryAdd(assembly.Name, assembly.Image);
            if (changed) Interlocked.Increment(ref s_Version);
            EnsureHooked();
        }
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
                lock (s_Gate) {
                    return s_Loaded.GetOrAdd(name, _ => Assembly.Load(image));
                }
            }
            return null;
        };
    }
}
