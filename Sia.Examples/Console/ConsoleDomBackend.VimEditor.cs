#if !BROWSER
namespace Sia_Examples.Console;

internal sealed partial class ConsoleDomBackend
{
    private readonly Dictionary<string, int> _editorSequences = [];
    private readonly Queue<Func<string>> _pendingEditorEvents = new();
    private EditMode _editMode = EditMode.None;
    private ConsoleDomNode? _editingSurface;
    private string? _pendingPrefix;

    private bool TryActivateEditor(ConsoleDomNode surface)
    {
        if (surface.Attributes.GetValueOrDefault("data-editor") is null) {
            return false;
        }
        _editingSurface = surface;
        _editMode = EditMode.Normal;
        _pendingPrefix = null;
        MarkChanged();
        return true;
    }

    private void ExitEditor()
    {
        _editMode = EditMode.None;
        _editingSurface = null;
        _pendingPrefix = null;
        MarkChanged();
    }

    private bool HandleEditorKey(ConsoleKeyInfo key)
        => _editMode == EditMode.Insert
            ? HandleInsertKey(key)
            : HandleNormalKey(key);

    private bool HandleInsertKey(ConsoleKeyInfo key)
    {
        switch (key.Key) {
            case ConsoleKey.Escape:
                _editMode = EditMode.Normal;
                MarkChanged();
                return true;
            case ConsoleKey.Enter:
                EnqueueEditorKey("Enter", false, false);
                return true;
            case ConsoleKey.Backspace:
                EnqueueEditorKey("Backspace", false, false);
                return true;
            case ConsoleKey.Delete:
                EnqueueEditorKey("Delete", false, false);
                return true;
            case ConsoleKey.Tab:
                EnqueueEditorKey("Tab", false, key.Modifiers.HasFlag(ConsoleModifiers.Shift));
                return true;
            case ConsoleKey.LeftArrow:
                EnqueueEditorKey("ArrowLeft", false, false);
                return true;
            case ConsoleKey.RightArrow:
                EnqueueEditorKey("ArrowRight", false, false);
                return true;
            case ConsoleKey.UpArrow:
                EnqueueEditorKey("ArrowUp", false, false);
                return true;
            case ConsoleKey.DownArrow:
                EnqueueEditorKey("ArrowDown", false, false);
                return true;
        }
        if (!char.IsControl(key.KeyChar)) {
            EnqueueEditorText(key.KeyChar.ToString());
            return true;
        }
        return false;
    }

    private bool HandleNormalKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) {
            ExitEditor();
            return true;
        }

        if (_pendingPrefix is { } prefix) {
            _pendingPrefix = null;
            if (prefix == "g" && key.KeyChar == 'g') {
                EnqueueEditorKey("Home", true, false);
                return true;
            }
            if (prefix == "d" && key.KeyChar == 'd') {
                EnqueueEditorKey("Home", false, false);
                EnqueueEditorKey("ArrowDown", false, true);
                EnqueueEditorKey("Backspace", false, false);
                return true;
            }
        }

        switch (key.KeyChar) {
            case 'h':
                EnqueueEditorKey("ArrowLeft", false, false);
                return true;
            case 'l':
                EnqueueEditorKey("ArrowRight", false, false);
                return true;
            case 'j':
                EnqueueEditorKey("ArrowDown", false, false);
                return true;
            case 'k':
                EnqueueEditorKey("ArrowUp", false, false);
                return true;
            case '0':
                EnqueueEditorKey("Home", false, false);
                return true;
            case '$':
                EnqueueEditorKey("End", false, false);
                return true;
            case 'w':
                EnqueueEditorKey("ArrowRight", true, false);
                return true;
            case 'b':
                EnqueueEditorKey("ArrowLeft", true, false);
                return true;
            case 'x':
                EnqueueEditorKey("Delete", false, false);
                return true;
            case 'g':
                _pendingPrefix = "g";
                return true;
            case 'd':
                _pendingPrefix = "d";
                return true;
            case 'G':
                EnqueueEditorKey("End", true, false);
                EnqueueEditorKey("Home", false, false);
                return true;
            case 'i':
                _editMode = EditMode.Insert;
                MarkChanged();
                return true;
            case 'a':
                _editMode = EditMode.Insert;
                EnqueueEditorKey("ArrowRight", false, false);
                return true;
            case 'I':
                _editMode = EditMode.Insert;
                EnqueueEditorKey("Home", false, false);
                return true;
            case 'A':
                _editMode = EditMode.Insert;
                EnqueueEditorKey("End", false, false);
                return true;
            case 'o':
                _editMode = EditMode.Insert;
                EnqueueEditorKey("End", false, false);
                EnqueueEditorKey("Enter", false, false);
                return true;
            case 'O':
                _editMode = EditMode.Insert;
                EnqueueEditorKey("Home", false, false);
                EnqueueEditorText("\n");
                EnqueueEditorKey("ArrowUp", false, false);
                return true;
        }
        return true;
    }

    private void EnqueueEditorKey(string key, bool control, bool shift)
        => _pendingEditorEvents.Enqueue(() =>
            $"key:{_editingSurface!.Attributes["data-editor"]}:{NextSequence()}:"
            + $"{CurrentSelectionArguments()}:{key}:{control}:{shift}:false");

    private void EnqueueEditorText(string text)
        => _pendingEditorEvents.Enqueue(() =>
            $"text:{_editingSurface!.Attributes["data-editor"]}:{NextSequence()}:"
            + $"{CurrentSelectionArguments()}:{Uri.EscapeDataString(text)}");

    private int NextSequence()
    {
        var cellId = _editingSurface!.Attributes["data-editor"];
        var next = _editorSequences.GetValueOrDefault(cellId) + 1;
        _editorSequences[cellId] = next;
        return next;
    }

    private string CurrentSelectionArguments()
        => _editingSurface!.Attributes.GetValueOrDefault("data-selection", "0:0:0:0");

    private EditCursor? ResolveEditCursor()
    {
        if (_editMode == EditMode.None || _editingSurface is null) {
            return null;
        }
        var parts = CurrentSelectionArguments().Split(':');
        if (parts.Length < 4
            || !int.TryParse(parts[2], out var headLine)
            || !int.TryParse(parts[3], out var headColumn)) {
            return null;
        }
        var line = _editingSurface.Children.FirstOrDefault(
            child => child.Attributes.GetValueOrDefault("data-ln") == headLine.ToString());
        return line is null ? null : new EditCursor(line, headColumn);
    }
}
#endif
