using Sia;

namespace Sia_Examples.Editor;


public readonly record struct SaveCommand : ICommand
{
    public void Execute(World world, Entity target) { }
}

public readonly record struct UndoCommand : ICommand
{
    public void Execute(World world, Entity target) { }
}

public readonly record struct RedoCommand : ICommand
{
    public void Execute(World world, Entity target) { }
}


public static class EditorYankBuffer
{
    public static string Text { get; set; } = "";
}
