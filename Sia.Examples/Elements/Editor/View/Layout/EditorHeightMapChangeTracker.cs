namespace Sia_Examples.Editor;

internal static class EditorHeightMapChangeTracker
{
    [ThreadStatic]
    private static bool _changed;

    public static bool Changed => _changed;

    public static void MarkChanged() => _changed = true;

    public static void Clear() => _changed = false;
}
