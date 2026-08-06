namespace Sia_Examples.Editor;

public sealed class KeyBinding
{
    public string? Key { get; init; }
    public string? Mac { get; init; }
    public string? Win { get; init; }
    public string? Linux { get; init; }
    public StateCommand? Run { get; init; }
    public StateCommand? Shift { get; init; }
    public bool PreventDefault { get; init; }
    public string Scope { get; init; } = "editor";
}

public static class StandardKeymap
{
    public static readonly KeyBinding[] Bindings =
    [
        new() { Key = "ArrowLeft", Run = CursorCommands.CharLeft, PreventDefault = true },
        new() { Key = "ArrowRight", Run = CursorCommands.CharRight, PreventDefault = true },
        new() { Key = "Ctrl-ArrowLeft", Run = CursorCommands.GroupLeft, PreventDefault = true },
        new() { Key = "Ctrl-ArrowRight", Run = CursorCommands.GroupRight, PreventDefault = true },
        new() { Key = "ArrowUp", Run = CursorCommands.LineUp, PreventDefault = true },
        new() { Key = "ArrowDown", Run = CursorCommands.LineDown, PreventDefault = true },
        new() { Key = "Home", Run = CursorCommands.LineStart, PreventDefault = true },
        new() { Key = "End", Run = CursorCommands.LineEnd, PreventDefault = true },
        new() { Key = "Ctrl-Home", Run = CursorCommands.DocStart },
        new() { Key = "Ctrl-End", Run = CursorCommands.DocEnd },
        new() { Key = "PageUp", Run = t => CursorCommands.PageUp(t, 20) },
        new() { Key = "PageDown", Run = t => CursorCommands.PageDown(t, 20) },
        new() { Key = "Enter", Run = LineCommands.InsertNewlineAndIndent },
        new() { Key = "Backspace", Run = DeleteCommands.CharBackward, PreventDefault = true },
        new() { Key = "Delete", Run = DeleteCommands.CharForward, PreventDefault = true },
        new() { Key = "Ctrl-Backspace", Run = DeleteCommands.GroupBackward, PreventDefault = true },
        new() { Key = "Ctrl-Delete", Run = DeleteCommands.GroupForward, PreventDefault = true },
        new() { Key = "Ctrl-a", Run = SelectionCommands.SelectAll },
        new() { Key = "Ctrl-z", Run = t => false, PreventDefault = true },

        new() { Key = "Ctrl-y", Run = t => false, PreventDefault = true },

        new() { Key = "Escape", Run = SelectionCommands.SimplifySelection },
        new() { Key = "Tab", Run = LineCommands.InsertTab, Shift = LineCommands.IndentLess },
        new() { Key = "Ctrl-]", Run = LineCommands.IndentMore },
        new() { Key = "Ctrl-[", Run = LineCommands.IndentLess },
        new() { Key = "Ctrl-/", Run = t => false },

    ];
}

public static class DefaultKeymap
{
    public static readonly KeyBinding[] Bindings = StandardKeymap.Bindings;
}
