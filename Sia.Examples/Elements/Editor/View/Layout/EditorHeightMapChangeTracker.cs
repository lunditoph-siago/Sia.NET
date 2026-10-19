namespace Sia.Examples.Editor;

internal static class EditorHeightMapChangeTracker
{
    [ThreadStatic]
    private static bool s_Changed;

    public static bool Changed => s_Changed;

    public static void MarkChanged() => s_Changed = true;

    public static void Clear() => s_Changed = false;
}
