using System.Text.RegularExpressions;

namespace Sia_Examples.Notebook;

public static class AssemblyReferenceResolver
{
    private static readonly Regex UsingDirective = new(
        @"(?m)^\s*(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?([A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.Compiled);

    public static IReadOnlySet<string> ResolveNamespaces(string source)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in UsingDirective.Matches(source)) {
            namespaces.Add(m.Groups[1].Value);
        }
        return namespaces;
    }

    public static IReadOnlySet<string> ResolveAssemblyNames(
        IEnumerable<string> namespaces, IReadOnlyCollection<string> availableAssemblyNames)
    {
        var byName = new HashSet<string>(availableAssemblyNames, StringComparer.OrdinalIgnoreCase);
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ns in namespaces) {
            var parts = ns.Split('.');
            for (var take = parts.Length; take >= 1; take--) {
                var candidate = string.Join('.', parts.Take(take));
                if (byName.TryGetValue(candidate, out var actual)) {
                    resolved.Add(actual);
                    break;
                }
            }
        }
        return resolved;
    }
}
