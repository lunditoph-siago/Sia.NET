using System.Runtime.CompilerServices;
using Sia;

namespace Sia_Examples.Editor;


public readonly record struct MoveLeftCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveLeft(doc.Value, cursor);
    }
}

public readonly record struct MoveRightCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveRight(doc.Value, cursor);
    }
}

public readonly record struct MoveUpCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveUp(doc.Value, cursor);
    }
}

public readonly record struct MoveDownCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveDown(doc.Value, cursor);
    }
}

public readonly record struct MoveWordLeftCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveWordLeft(doc.Value, cursor);
    }
}

public readonly record struct MoveWordRightCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveWordRight(doc.Value, cursor);
    }
}

public readonly record struct MoveLineStartCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        cursor = CursorOps.MoveLineStart(cursor);
    }
}

public readonly record struct MoveLineEndCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveLineEnd(doc.Value, cursor);
    }
}

public readonly record struct MoveFirstNonWhitespaceCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveFirstNonWhitespace(doc.Value, cursor);
    }
}

public readonly record struct MoveStartCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        cursor = CursorOps.MoveStart(cursor);
    }
}

public readonly record struct MoveEndCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.MoveEnd(doc.Value, cursor);
    }
}

public readonly record struct PageUpCommand(int PageSize) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.PageUp(doc.Value, cursor, PageSize);
    }
}

public readonly record struct PageDownCommand(int PageSize) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var doc = ref target.Get<EditorDoc>();
        cursor = CursorOps.PageDown(doc.Value, cursor, PageSize);
    }
}


public readonly record struct InsertCharCommand(char Char) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.InsertChar(docComp.Value, cursor, Char);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct InsertNewLineCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.InsertNewLine(docComp.Value, cursor);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct InsertTabCommand(int TabSize = 4) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.InsertTab(docComp.Value, cursor, TabSize);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct DeleteLeftCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.DeleteLeft(docComp.Value, cursor);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct DeleteRightCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.DeleteRight(docComp.Value, cursor);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct DeleteWordLeftCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.DeleteWordLeft(docComp.Value, cursor);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct DeleteWordRightCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.DeleteWordRight(docComp.Value, cursor);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct DeleteLineCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.DeleteLine(docComp.Value, cursor);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct DeleteSelectionCommand : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.DeleteSelection(docComp.Value, cursor);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct IndentCommand(int TabSize = 4) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.Indent(docComp.Value, cursor, TabSize);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct OutdentCommand(int TabSize = 4) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.Outdent(docComp.Value, cursor, TabSize);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}

public readonly record struct PasteCommand(string Clipboard) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        ref var docComp = ref target.Get<EditorDoc>();
        var (newDoc, newCursor) = EditOps.Paste(docComp.Value, cursor, Clipboard);
        docComp.Apply(newDoc);
        cursor = newCursor;
    }
}


public readonly record struct SetModeCommand(EditorMode Mode) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        cursor = cursor with { Mode = Mode };
    }
}

public readonly record struct ToggleSelectionCommand(int AnchorLine, int AnchorColumn) : ICommand
{
    public void Execute(World world, Entity target)
    {
        ref var cursor = ref target.Get<CursorState>();
        cursor = cursor.HasSelection
            ? cursor.WithoutSelection()
            : cursor with { AnchorLine = AnchorLine, AnchorColumn = AnchorColumn };
    }
}


public static class CursorOps
{
    public static CursorState MoveLeft(EditorDocument doc, CursorState c)
    {
        if (c.HasSelection) { var (sl, sc) = c.SelectionStart; return ClearSelection(c, sl, sc); }
        if (c.Column > 0) return Reposition(c, c.Line, c.Column - 1);
        if (c.Line > 0) return Reposition(c, c.Line - 1, doc.LineLength(c.Line - 1));
        return c;
    }

    public static CursorState MoveRight(EditorDocument doc, CursorState c)
    {
        if (c.HasSelection) { var (el, ec) = c.SelectionEnd; return ClearSelection(c, el, ec); }
        if (c.Column < doc.LineLength(c.Line)) return Reposition(c, c.Line, c.Column + 1);
        if (c.Line + 1 < doc.LineCount) return Reposition(c, c.Line + 1, 0);
        return c;
    }

    public static CursorState MoveUp(EditorDocument doc, CursorState c)
    {
        if (c.Line == 0) return c;
        var newLine = c.Line - 1;
        var newCol = Math.Min(c.PreferredColumn, doc.LineLength(newLine));
        return c.WithoutSelection() with { Line = newLine, Column = newCol };
    }

    public static CursorState MoveDown(EditorDocument doc, CursorState c)
    {
        if (c.Line + 1 >= doc.LineCount) return c;
        var newLine = c.Line + 1;
        var newCol = Math.Min(c.PreferredColumn, doc.LineLength(newLine));
        return c.WithoutSelection() with { Line = newLine, Column = newCol };
    }

    public static CursorState MoveLineStart(CursorState c)
        => c.WithoutSelection() with { Column = 0, PreferredColumn = 0 };

    public static CursorState MoveLineEnd(EditorDocument doc, CursorState c)
    {
        var col = doc.LineLength(c.Line);
        return c.WithoutSelection() with { Column = col, PreferredColumn = col };
    }

    public static CursorState MoveFirstNonWhitespace(EditorDocument doc, CursorState c)
    {
        var line = doc[c.Line];
        var col = 0;
        while (col < line.Length && char.IsWhiteSpace(line[col])) col++;
        return c.WithoutSelection() with { Column = col, PreferredColumn = col };
    }

    public static CursorState MoveStart(CursorState c)
        => c.WithoutSelection() with { Line = 0, Column = 0, PreferredColumn = 0 };

    public static CursorState MoveEnd(EditorDocument doc, CursorState c)
    {
        var lastLine = doc.LineCount - 1;
        var lastCol = doc.LineLength(lastLine);
        return c.WithoutSelection() with { Line = lastLine, Column = lastCol, PreferredColumn = lastCol };
    }

    public static CursorState MoveWordLeft(EditorDocument doc, CursorState c)
    {
        var (line, col) = (c.Line, c.Column);
        while (line > 0 || col > 0)
        {
            if (col == 0) { line--; col = doc.LineLength(line); continue; }
            if (!IsWordChar(doc[line][col - 1])) { col--; continue; }
            while (col > 0 && IsWordChar(doc[line][col - 1])) col--;
            break;
        }
        return c.WithoutSelection() with { Line = line, Column = col, PreferredColumn = col };
    }

    public static CursorState MoveWordRight(EditorDocument doc, CursorState c)
    {
        var (line, col) = (c.Line, c.Column);
        while (line < doc.LineCount - 1 || col < doc.LineLength(line))
        {
            if (col >= doc.LineLength(line)) { line++; col = 0; continue; }
            if (!IsWordChar(doc[line][col])) { col++; continue; }
            while (col < doc.LineLength(line) && IsWordChar(doc[line][col])) col++;
            break;
        }
        return c.WithoutSelection() with { Line = line, Column = col, PreferredColumn = col };
    }

    public static CursorState PageUp(EditorDocument doc, CursorState c, int pageSize)
    {
        var newLine = Math.Max(c.Line - pageSize, 0);
        var newCol = Math.Min(c.PreferredColumn, doc.LineLength(newLine));
        return c.WithoutSelection() with { Line = newLine, Column = newCol, ScrollLine = Math.Max(c.ScrollLine - pageSize, 0) };
    }

    public static CursorState PageDown(EditorDocument doc, CursorState c, int pageSize)
    {
        var newLine = Math.Min(c.Line + pageSize, doc.LineCount - 1);
        var newCol = Math.Min(c.PreferredColumn, doc.LineLength(newLine));
        return c.WithoutSelection() with { Line = newLine, Column = newCol, ScrollLine = Math.Min(c.ScrollLine + pageSize, Math.Max(doc.LineCount - 1, 0)) };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static CursorState ClearSelection(CursorState c, int line, int col)
        => c.WithoutSelection() with { Line = line, Column = col, PreferredColumn = col };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CursorState Reposition(CursorState c, int line, int col)
        => c with { Line = line, Column = col, PreferredColumn = col };
}

public static class EditOps
{
    public static (EditorDocument Doc, CursorState Cursor) InsertChar(
        EditorDocument doc, CursorState c, char ch)
    {
        if (c.HasSelection) { var (d, c2) = DeleteSelection(doc, c); return InsertChar(d, c2, ch); }
        doc.Insert(c.Line, c.Column, ch.ToString());
        return (doc, c with { Column = c.Column + 1, PreferredColumn = c.Column + 1 });
    }

    public static (EditorDocument Doc, CursorState Cursor) InsertNewLine(
        EditorDocument doc, CursorState c)
    {
        if (c.HasSelection) { var (d, c2) = DeleteSelection(doc, c); return InsertNewLine(d, c2); }
        doc.Insert(c.Line, c.Column, "\n");
        var indent = AutoIndent(doc, c.Line + 1);
        var len = indent.Length;
        if (len > 0) doc.Insert(c.Line + 1, 0, indent);
        return (doc, c with { Line = c.Line + 1, Column = len, PreferredColumn = len });
    }

    public static (EditorDocument Doc, CursorState Cursor) InsertTab(
        EditorDocument doc, CursorState c, int tabSize = 4)
    {
        var spaces = tabSize - (c.Column % tabSize);
        doc.Insert(c.Line, c.Column, new string(' ', spaces));
        return (doc, c with { Column = c.Column + spaces, PreferredColumn = c.Column + spaces });
    }

    public static (EditorDocument Doc, CursorState Cursor) DeleteLeft(
        EditorDocument doc, CursorState c)
    {
        if (c.HasSelection) return DeleteSelection(doc, c);
        if (c.Column == 0 && c.Line == 0) return (doc, c);
        if (c.Column == 0)
        {
            var prevLen = doc.LineLength(c.Line - 1);
            doc.DeleteRange(c.Line - 1, prevLen, c.Line, 0);
            return (doc, c with { Line = c.Line - 1, Column = prevLen, PreferredColumn = prevLen });
        }
        doc.Delete(c.Line, c.Column - 1, 1);
        return (doc, c with { Column = c.Column - 1, PreferredColumn = c.Column - 1 });
    }

    public static (EditorDocument Doc, CursorState Cursor) DeleteRight(
        EditorDocument doc, CursorState c)
    {
        if (c.HasSelection) return DeleteSelection(doc, c);
        if (c.Column >= doc.LineLength(c.Line))
        {
            if (c.Line + 1 >= doc.LineCount) return (doc, c);
            doc.DeleteRange(c.Line, c.Column, c.Line + 1, 0);
            return (doc, c);
        }
        doc.Delete(c.Line, c.Column, 1);
        return (doc, c);
    }

    public static (EditorDocument Doc, CursorState Cursor) DeleteWordLeft(
        EditorDocument doc, CursorState c)
    {
        if (c.HasSelection) return DeleteSelection(doc, c);
        var end = CursorOps.MoveWordLeft(doc, c);
        doc.DeleteRange(end.Line, end.Column, c.Line, c.Column);
        return (doc, c with { Line = end.Line, Column = end.Column, PreferredColumn = end.Column });
    }

    public static (EditorDocument Doc, CursorState Cursor) DeleteWordRight(
        EditorDocument doc, CursorState c)
    {
        if (c.HasSelection) return DeleteSelection(doc, c);
        var end = CursorOps.MoveWordRight(doc, c);
        doc.DeleteRange(c.Line, c.Column, end.Line, end.Column);
        return (doc, c);
    }

    public static (EditorDocument Doc, CursorState Cursor) DeleteLine(
        EditorDocument doc, CursorState c)
    {
        if (doc.LineCount == 1)
        {
            doc.Delete(0, 0, doc.LineLength(0));
            var cleared = c with { Line = 0, Column = 0, PreferredColumn = 0 };
            return (doc, cleared.WithoutSelection());
        }
        if (c.Line + 1 < doc.LineCount)
        {
            doc.DeleteRange(c.Line, 0, c.Line + 1, 0);
            var newCol = Math.Min(c.PreferredColumn, doc.LineLength(c.Line));
            var updated = c with { Column = newCol, PreferredColumn = newCol };
            return (doc, updated.WithoutSelection());
        }
        doc.DeleteRange(c.Line - 1, doc.LineLength(c.Line - 1), c.Line, doc.LineLength(c.Line));
        var col = doc.LineLength(c.Line - 1);
        var repositioned = c with { Line = c.Line - 1, Column = col, PreferredColumn = col };
        return (doc, repositioned.WithoutSelection());
    }

    public static (EditorDocument Doc, CursorState Cursor) DeleteSelection(
        EditorDocument doc, CursorState c)
    {
        if (!c.HasSelection) return (doc, c);
        var (sl, sc) = c.SelectionStart;
        var (el, ec) = c.SelectionEnd;
        doc.DeleteRange(sl, sc, el, ec);
        return (doc, c.WithoutSelection() with { Line = sl, Column = sc, PreferredColumn = sc });
    }

    public static (EditorDocument Doc, CursorState Cursor) Indent(
        EditorDocument doc, CursorState c, int tabSize = 4)
    {
        var (startLine, _) = c.HasSelection ? c.SelectionStart : (c.Line, 0);
        var (endLine, _) = c.HasSelection ? c.SelectionEnd : (c.Line, 0);
        for (var i = startLine; i <= endLine && i < doc.LineCount; i++)
            doc.Insert(i, 0, new string(' ', tabSize));
        var newCol = c.Column + (c.HasSelection ? 0 : tabSize);
        return (doc, c with { Column = newCol, PreferredColumn = newCol });
    }

    public static (EditorDocument Doc, CursorState Cursor) Outdent(
        EditorDocument doc, CursorState c, int tabSize = 4)
    {
        var (startLine, _) = c.HasSelection ? c.SelectionStart : (c.Line, 0);
        var (endLine, _) = c.HasSelection ? c.SelectionEnd : (c.Line, 0);
        for (var i = startLine; i <= endLine && i < doc.LineCount; i++)
        {
            var line = doc[i]; var spaces = 0;
            while (spaces < tabSize && spaces < line.Length && line[spaces] == ' ') spaces++;
            if (spaces > 0) doc.Delete(i, 0, spaces);
        }
        var newCol = Math.Max(0, c.Column - (c.HasSelection ? 0 : tabSize));
        return (doc, c with { Column = newCol, PreferredColumn = newCol });
    }

    public static (EditorDocument Doc, CursorState Cursor) Paste(
        EditorDocument doc, CursorState c, string clipboard)
    {
        if (string.IsNullOrEmpty(clipboard)) return (doc, c);
        if (c.HasSelection) { var (d, c2) = DeleteSelection(doc, c); return Paste(d, c2, clipboard); }
        doc.Insert(c.Line, c.Column, clipboard);
        var parts = clipboard.Replace("\r\n", "\n").Split('\n');
        var newLine = c.Line + parts.Length - 1;
        var newCol = parts.Length == 1 ? c.Column + parts[0].Length : parts[^1].Length;
        return (doc, c with { Line = newLine, Column = newCol, PreferredColumn = newCol });
    }

    public static string GetSelectedText(EditorDocument doc, CursorState c)
    {
        if (!c.HasSelection) return "";
        var (sl, sc) = c.SelectionStart;
        var (el, ec) = c.SelectionEnd;
        if (sl == el) return doc[sl].Substring(sc, ec - sc);
        var sb = new System.Text.StringBuilder();
        sb.Append(doc[sl][sc..]).Append('\n');
        for (var i = sl + 1; i < el; i++) sb.Append(doc[i]).Append('\n');
        sb.Append(doc[el][..ec]);
        return sb.ToString();
    }

    private static string AutoIndent(EditorDocument doc, int line)
    {
        if (line <= 0) return "";
        var prev = doc[line - 1];
        var indent = 0;
        while (indent < prev.Length && prev[indent] == ' ') indent++;
        var trimmed = prev.TrimStart();
        if (trimmed.EndsWith('{') || trimmed.EndsWith('(')) return new string(' ', indent + 4);
        return new string(' ', indent);
    }
}
