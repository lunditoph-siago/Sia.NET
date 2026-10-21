using System.Globalization;
using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    private EditorKeymap _keymap = EditorKeymap.Default;
    private EditorInputMode _standaloneMode = EditorInputMode.Standard;
    private BrowserEditorRegistry? _inputSession;
    private ModalPending _modalPending;
    private CommandBinding[] _bindings = EditorKeymap.Default.Compile();
    private CommandContext _commandContext = CommandContext.Editor;
    private readonly Queue<EditorCommand> _workbenchCommands = [];

    private EditorInputMode InputMode => _inputSession?.InputMode ?? _standaloneMode;

    internal void ConfigureKeyboard(EditorKeymap keymap, CommandContext context = CommandContext.Editor)
    {
        SynchronizeNativeInput();
        var changed = _keymap.Profile != keymap.Profile;
        _keymap = keymap;
        _bindings = keymap.Compile();
        _commandContext = context;
        _modalPending = default;
        if (changed) {
            EndHistoryGroup();
            CancelCompletion(); CancelSignature();
            if (_inputSession is null) ChangeInputMode(keymap.InitialMode);
            if (InputMode == EditorInputMode.Normal) {
                var state = State.Value;
                Commit(state.Apply(new() { Selection = EditorSelection.Single(ModalEditing.Clamp(state.Doc, state.Selection.Main.Head)) }));
            }
        }
        ProjectInputMode();
    }

    internal bool TryTakeWorkbenchCommand(out EditorCommand command) => _workbenchCommands.TryDequeue(out command!);

    private void ProjectInputMode() => _view.SetInputMode(_keymap.Profile, InputMode);

    private void ChangeInputMode(EditorInputMode mode)
    {
        if (_inputSession is { } session) session.SetInputMode(mode);
        else _standaloneMode = mode;
        ProjectInputMode();
    }

    internal void BindInputSession(BrowserEditorRegistry session)
    {
        _inputSession = session;
        ProjectInputMode();
    }

    internal void ResetInputState()
    {
        _modalPending = default;
        EndHistoryGroup();
        CancelCompletion(); CancelSignature();
        ProjectInputMode();
    }

    internal void EndInputTarget()
    {
        SynchronizeNativeInput();
        ResetInputState();
        if (InputMode == EditorInputMode.Visual)
            ExecuteModalCommand(EditorCommand.Get(EditorCommandId.Escape), new(-1, -1, -1, -1), null);
    }

    private EditGroup InputHistoryGroup(EditGroup ordinary)
        => _keymap.Profile == KeyboardProfile.Vim && InputMode == EditorInputMode.Insert ? EditGroup.InsertSession : ordinary;

    private bool HandleStroke(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var input)) return false;
        try {
            var parts = input.Split(':', 8);
            if (parts.Length != 8 || !TryReadSelection(parts, 0, out var selection)
                || !int.TryParse(parts[5], out var flags) || flags < 0 || flags > 15
                || !bool.TryParse(parts[7], out var repeat)) return false;
            var key = Uri.UnescapeDataString(parts[4]);
            var code = Uri.UnescapeDataString(parts[6]);
            return RunStroke(key, (ShortcutModifiers)flags, code, repeat, selection);
        }
        finally { DomRuntime.AcknowledgeEditorCommand(_cellId, sequence); }
    }

    private bool RunStroke(string text, ShortcutModifiers modifiers, string code, bool repeat, BrowserSelection selection)
    {
        var before = State.Value;
        var selected = ApplyNativeSelection(before, selection);
        if (selected != before) Commit(selected);
        var key = InputMode is EditorInputMode.Normal or EditorInputMode.Visual
            && (modifiers & ~ShortcutModifiers.Shift) == 0 ? ModalInput.Normalize(text, modifiers)
            : new KeyStroke(text == " " ? "Space" : text.Length == 1 ? text.ToUpperInvariant() : text, modifiers);
        if (text == "Escape") return ExecuteCommand(EditorCommand.Get(EditorCommandId.Escape), new(-1, -1, -1, -1), _completionGeneration);
        var context = _completion is { HasItems: true } ? CommandContext.Suggestions
            : _signatureHelp is not null ? CommandContext.Signatures : _commandContext;
        var hadPrefix = _modalPending.Prefix is not null;
        KeyStroke? physical = code.Length == 0 ? null : new($"[{code.ToUpperInvariant()}]", modifiers);
        var result = ModalInput.Parse(_modalPending, key, _bindings, InputMode, context, physical);
        _modalPending = result.Pending;
        if (result.Invocation is { } invocation) {
            var command = EditorCommand.Get(invocation.Id);
            if (!command.IsEditor) {
                if (!repeat) _workbenchCommands.Enqueue(command);
                return true;
            }
            var fallback = context == CommandContext.Suggestions
                ? ModalInput.Parse(default, key, _bindings, InputMode, _commandContext, physical).Invocation : null;
            return ExecuteCommand(command, new(-1, -1, -1, -1), _completionGeneration,
                fallback is { } alternative ? EditorCommand.Get(alternative.Id) : null, invocation);
        }
        if (InputMode is EditorInputMode.Standard or EditorInputMode.Insert && !hadPrefix && result.Pending == default
            && text.Length > 0 && (modifiers & ~ShortcutModifiers.Shift) == 0 && StringInfo.GetNextTextElementLength(text) == text.Length)
            return ApplyTextInput(new(-1, -1, -1, -1), text, isolateHistory: false);
        return true;
    }

    private bool RunModalText(BrowserSelection selection, string text)
    {
        if (text.Length == 0 || StringInfo.GetNextTextElementLength(text) != text.Length) {
            _modalPending = default;
            return true;
        }
        var selected = ApplyNativeSelection(State.Value, selection);
        if (selected != State.Value) Commit(selected);
        return RunStroke(text, ShortcutModifiers.None, "", false, new(-1, -1, -1, -1));
    }

    private bool HandleKey(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) {
            return false;
        }

        try {
            var parts = commandArguments.Split(':', 8);
            if (parts.Length < 8
                || !TryReadSelection(parts, 0, out var nativeSelection)
                || !bool.TryParse(parts[5], out var control)
                || !bool.TryParse(parts[6], out var shift)
                || !bool.TryParse(parts[7], out var alt)) {
                return false;
            }
            var modifiers = (control ? ShortcutModifiers.Primary : ShortcutModifiers.None)
                | (alt ? ShortcutModifiers.Alt : ShortcutModifiers.None) | (shift ? ShortcutModifiers.Shift : ShortcutModifiers.None);
            var key = parts[4] == " " ? "Space" : parts[4].Length == 1 ? parts[4].ToUpperInvariant() : parts[4];
            var context = _completion is { HasItems: true } ? CommandContext.Suggestions
                : _signatureHelp is not null ? CommandContext.Signatures : CommandContext.Editor;
            var command = EditorKeymap.Resolve(_bindings, new(key, modifiers), context);
            var fallback = context == CommandContext.Suggestions ? EditorKeymap.Resolve(_bindings, new(key, modifiers), CommandContext.Editor) : null;
            return command?.IsEditor == true && ExecuteCommand(command, nativeSelection, _completionGeneration, fallback);
        }
        finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleCommand(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) return false;
        try {
            var parts = commandArguments.Split(':', 7);
            if (parts.Length < 5 || !TryReadSelection(parts, 0, out var selection)
                || EditorCommand.Find(parts[4]) is not { IsEditor: true } command) return false;
            var fallback = parts.Length > 5 ? EditorCommand.Find(parts[5]) : null;
            var generation = parts.Length > 6 && int.TryParse(parts[6], out var value) ? value : -1;
            return ExecuteCommand(command, selection, generation, fallback);
        }
        finally { DomRuntime.AcknowledgeEditorCommand(_cellId, sequence); }
    }

}
