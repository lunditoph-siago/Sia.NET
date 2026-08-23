namespace Sia_Examples.Notebook;

public static class WorkspacePath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("A workspace path must not be empty.", nameof(path));
        }
        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.Length > 1 && trimmed.EndsWith('/')) {
            trimmed = trimmed[..^1];
        }
        if (trimmed.Length == 0 || trimmed.StartsWith('/')) {
            throw new ArgumentException($"'{path}' is not a valid workspace path.", nameof(path));
        }
        foreach (var segment in trimmed.Split('/')) {
            if (segment.Length == 0 || segment is "." or "..") {
                throw new ArgumentException($"'{path}' is not a valid workspace path.", nameof(path));
            }
        }
        return trimmed;
    }

    public static bool TryNormalize(string path, out string normalized)
    {
        try {
            normalized = Normalize(path);
            return true;
        }
        catch (ArgumentException) {
            normalized = string.Empty;
            return false;
        }
    }

    public static string? Parent(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? null : path[..separator];
    }

    public static string Name(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    public static string Combine(string parent, string name)
        => parent.Length == 0 ? name : parent + "/" + name;

    public static bool IsDescendantOf(string path, string ancestor)
        => path.Length > ancestor.Length + 1
            && path.StartsWith(ancestor + "/", StringComparison.Ordinal);

    public static bool EqualsPath(string left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);
}
