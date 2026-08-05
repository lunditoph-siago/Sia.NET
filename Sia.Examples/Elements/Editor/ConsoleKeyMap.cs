#if !BROWSER
using Sia;

namespace Sia_Examples.Editor;

public static class ConsoleKeyMap
{
    public static ICommand? Map(ConsoleKeyInfo key, EditorMode mode)
        => mode switch
        {
            EditorMode.Insert => MapInsert(key),
            EditorMode.Normal => MapNormal(key),
            EditorMode.Visual or EditorMode.VisualLine => MapVisual(key),
            _ => null,
        };

    private static ICommand? MapInsert(ConsoleKeyInfo key)
        => (key.Modifiers, key.Key) switch
        {
            (ConsoleModifiers.Control, ConsoleKey.S) => new SaveCommand(),
            (0, ConsoleKey.Escape) => new SetModeCommand(EditorMode.Normal),
            (0, ConsoleKey.Enter) => new InsertNewLineCommand(),
            (0, ConsoleKey.Backspace) => new DeleteLeftCommand(),
            (0, ConsoleKey.Delete) => new DeleteRightCommand(),
            (0, ConsoleKey.LeftArrow) => new MoveLeftCommand(),
            (0, ConsoleKey.RightArrow) => new MoveRightCommand(),
            (0, ConsoleKey.UpArrow) => new MoveUpCommand(),
            (0, ConsoleKey.DownArrow) => new MoveDownCommand(),
            (0, ConsoleKey.Home) => new MoveLineStartCommand(),
            (0, ConsoleKey.End) => new MoveLineEndCommand(),
            (0, ConsoleKey.PageUp) => new PageUpCommand(20),
            (0, ConsoleKey.PageDown) => new PageDownCommand(20),
            (0, ConsoleKey.Tab) => new InsertTabCommand(4),
            _ when key.KeyChar >= ' ' => new InsertCharCommand(key.KeyChar),
            _ => null,
        };

    private static ICommand? MapNormal(ConsoleKeyInfo key)
    {
        if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.S)
            return new SaveCommand();

        return key.Key switch
        {
            ConsoleKey.I => new SetModeCommand(EditorMode.Insert),
            ConsoleKey.A => new MoveRightThenInsertCommand(),
            ConsoleKey.O => new OpenLineBelowCommand(),
            ConsoleKey.H => new MoveLeftCommand(),
            ConsoleKey.J => new MoveDownCommand(),
            ConsoleKey.K => new MoveUpCommand(),
            ConsoleKey.L => new MoveRightCommand(),
            ConsoleKey.W => new MoveWordRightCommand(),
            ConsoleKey.B => new MoveWordLeftCommand(),
            ConsoleKey.X => new DeleteRightCommand(),
            ConsoleKey.D => new DeleteLineCommand(),
            ConsoleKey.P => new PasteFromYankCommand(),
            ConsoleKey.Y => new YankLineCommand(),
            ConsoleKey.V => new SetModeCommand(EditorMode.Visual),
            ConsoleKey.G when key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                => new MoveEndCommand(),
            _ => null,
        };
    }

    private static ICommand? MapVisual(ConsoleKeyInfo key)
        => key.Key switch
        {
            ConsoleKey.Escape => new SetModeCommand(EditorMode.Normal),
            ConsoleKey.H => new MoveLeftCommand(),
            ConsoleKey.J => new MoveDownCommand(),
            ConsoleKey.K => new MoveUpCommand(),
            ConsoleKey.L => new MoveRightCommand(),
            ConsoleKey.W => new MoveWordRightCommand(),
            ConsoleKey.B => new MoveWordLeftCommand(),
            ConsoleKey.X or ConsoleKey.D => new CutSelectionCommand(),
            ConsoleKey.Y => new YankSelectionCommand(),
            _ => null,
        };
}


public readonly record struct MoveRightThenInsertCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveRight(doc.Value, cursor);
        cursor = cursor with { Mode = EditorMode.Insert };
    }
}

public readonly record struct OpenLineBelowCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var doc = docComp.Value;
        var endCol = doc.LineLength(cursor.Line);
        doc.Insert(cursor.Line, endCol, "\n");
        docComp.Apply(doc);
        cursor = new CursorState
        {
            Line = cursor.Line + 1, Column = 0,
            PreferredColumn = 0, Mode = EditorMode.Insert,
        };
    }
}

public readonly record struct YankLineCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var docComp = ref target.Get<EditorDoc>();
        ref var cursor = ref target.Get<CursorState>();
        EditorYankBuffer.Text = docComp.Value[cursor.Line] + "\n";
    }
}

public readonly record struct YankSelectionCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var docComp = ref target.Get<EditorDoc>();
        ref var cursor = ref target.Get<CursorState>();
        if (cursor.HasSelection)
            EditorYankBuffer.Text = EditOps.GetSelectedText(docComp.Value, cursor);
    }
}

public readonly record struct CutSelectionCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var docComp = ref target.Get<EditorDoc>();
        ref var cursor = ref target.Get<CursorState>();
        if (cursor.HasSelection)
        {
            EditorYankBuffer.Text = EditOps.GetSelectedText(docComp.Value, cursor);
            var (newDoc, newCursor) = EditOps.DeleteSelection(docComp.Value, cursor);
            docComp.Apply(newDoc);
            cursor = newCursor;
        }
    }
}

public readonly record struct PasteFromYankCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        if (string.IsNullOrEmpty(EditorYankBuffer.Text)) return;
        ref var docComp = ref target.Get<EditorDoc>();
        ref var cursor = ref target.Get<CursorState>();
        var (newDoc, newCursor) = EditOps.Paste(docComp.Value, cursor, EditorYankBuffer.Text);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

#endif
