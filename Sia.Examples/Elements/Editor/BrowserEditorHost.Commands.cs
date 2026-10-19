using Sia.Examples.Dom;
using Sia.Reactive;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    internal bool CanExecuteCommand(EditorCommand command) => command.Context switch {
        CommandContext.Editor => true,
        CommandContext.Suggestions => _completion is { HasItems: true } completion && (command.Id != EditorCommandId.AcceptSuggestion
            || completion.Items[_completionIndex].Edit is not null && ReferenceEquals(_completionDocument, State.Value.Doc)
            && State.Value.Selection.Main.Empty && State.Value.Selection.Main.Head == _completionPosition),
        CommandContext.Signatures => _signatureHelp is not null,
        _ => false,
    };

    internal bool ExecuteWorkbenchCommand(EditorCommand command)
    {
        SynchronizeNativeInput();
        return CanExecuteCommand(command) && ExecuteCommand(command, new(-1, -1, -1, -1), _completionGeneration);
    }

    private bool HandleKey(string arguments)
    {
        if (!TryReadCommand(arguments, out var sequence, out var commandArguments)) {
            return false;
        }

        try {
            return HandleKeyCommand(commandArguments);
        }
        finally {
            DomRuntime.AcknowledgeEditorCommand(_cellId, sequence);
        }
    }

    private bool HandleKeyCommand(string arguments)
    {
        var parts = arguments.Split(':', 8);
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
        var command = EditorKeymap.Default.Resolve(new(key, modifiers), context);
        var fallback = context == CommandContext.Suggestions ? EditorKeymap.Default.Resolve(new(key, modifiers), CommandContext.Editor) : null;
        return command?.IsEditor == true && ExecuteCommand(command, nativeSelection, _completionGeneration, fallback);
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

    private bool ExecuteCommand(EditorCommand command, BrowserSelection nativeSelection, int generation,
        EditorCommand? fallback = null)
    {
        if (command.Context == CommandContext.Suggestions) {
            if (generation != _completionGeneration || _completion is not { HasItems: true })
                return fallback is { IsEditor: true, Context: not CommandContext.Suggestions }
                    && ExecuteCommand(fallback, nativeSelection, generation);
            switch (command.Id) {
                case EditorCommandId.AcceptSuggestion:
                    if (_completion.Items[_completionIndex].Edit is null || !ReferenceEquals(_completionDocument, State.Value.Doc)
                        || !State.Value.Selection.Main.Empty || State.Value.Selection.Main.Head != _completionPosition || fallback?.Id == EditorCommandId.Newline
                        && (_completionSoft || !CompletionMakesEdit(_completion.Items[_completionIndex]))) {
                        CancelCompletion();
                        return fallback is { IsEditor: true } && ExecuteCommand(fallback, nativeSelection, generation);
                    }
                    AcceptCompletion(_completion); break;
                case EditorCommandId.NextSuggestion: SelectCompletion((_completionIndex + 1) % _completion.Items.Count); break;
                case EditorCommandId.PreviousSuggestion: SelectCompletion((_completionIndex - 1 + _completion.Items.Count) % _completion.Items.Count); break;
                case EditorCommandId.NextSuggestionPage: SelectCompletion(System.Math.Min(_completionIndex + _completionPageSize, _completion.Items.Count - 1)); break;
                case EditorCommandId.PreviousSuggestionPage: SelectCompletion(System.Math.Max(0, _completionIndex - _completionPageSize)); break;
                case EditorCommandId.HideSuggestions: CancelCompletion(); break;
            }
            return true;
        }
        if (command.Id is EditorCommandId.Undo or EditorCommandId.Redo)
            return ReplayHistory(command.Id == EditorCommandId.Redo);
        if (command.Id is EditorCommandId.Find or EditorCommandId.Replace or EditorCommandId.NextMatch or EditorCommandId.PreviousMatch)
            return ExecuteFindCommand(command.Id, nativeSelection);
        if (command.Id == EditorCommandId.Escape && _find.IsOpen) { _find.Close(); _view.SetFindMatch(false); return true; }
        if (command.Id is EditorCommandId.GoToDefinition or EditorCommandId.FindReferences or EditorCommandId.RenameSymbol or EditorCommandId.FormatDocument) return StartLanguageCommand(command, nativeSelection);
        if (command.Id == EditorCommandId.Suggest) return HandleManualCompletion(nativeSelection);
        if (command.Id == EditorCommandId.SignatureHelp) {
            var state = ApplyNativeSelection(State.Value, nativeSelection);
            Commit(state);
            _view.RevealSelection();
            ScheduleSignature(state, manual: true);
            return true;
        }
        if (command.Context == CommandContext.Signatures) {
            if (command.Id == EditorCommandId.HideSignature) CancelSignature();
            else StepSignature(command.Id == EditorCommandId.NextSignature ? 1 : -1);
            return true;
        }
        if (command.Id == EditorCommandId.Escape && _signatureRequested && _completion is null) {
            CancelCompletion();
            CancelSignature();
            return true;
        }
        if (command.Edit is not { } edit) return false;
        if ((_completionPending || _completion is not null) && command.Id is
            EditorCommandId.Indent or EditorCommandId.Outdent or EditorCommandId.Newline or EditorCommandId.Escape
            or EditorCommandId.Up or EditorCommandId.Down or EditorCommandId.SelectUp or EditorCommandId.SelectDown) {
            CancelCompletion();
            if (command.Id == EditorCommandId.Escape) return true;
        }
        var before = State.Value;
        var editBefore = ApplyNativeSelection(before, nativeSelection);
        var after = editBefore;
        if (!edit(new(after, state => after = state))) {
            var viewport = EnsureSelectionVisible(after, Viewport.Value);
            _view.RevealSelection();
            ApplyViewport(viewport);
            _world.FlushReactive();
            return false;
        }
        Commit(after, keyboardNavigation: ReferenceEquals(after.Doc, before.Doc)
            && !after.Selection.Eq(before.Selection, true), historySelection: editBefore.Selection,
            historyGroup: editBefore.Selection.Ranges.All(static range => range.Empty)
                ? command.Id switch { EditorCommandId.Backspace => EditGroup.Backward, EditorCommandId.Delete => EditGroup.Forward, _ => EditGroup.None }
                : EditGroup.None);
        ScheduleCompletion(before, after);
        return true;
    }
}
