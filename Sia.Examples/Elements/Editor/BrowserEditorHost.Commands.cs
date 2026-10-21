using Sia.Examples.Dom;
using Sia.Reactive;

namespace Sia.Examples.Editor;

public sealed partial class BrowserEditorHost
{
    internal bool CanExecuteCommand(EditorCommand command) => (!command.Modal || _keymap.Profile == KeyboardProfile.Vim) && (command.Context switch {
        CommandContext.Editor => true,
        CommandContext.Suggestions => _completion is { HasItems: true }
            && (command.Id != EditorCommandId.AcceptSuggestion || CanAcceptCompletion(State.Value)),
        CommandContext.Signatures => _signatureHelp is not null,
        _ => false,
    });

    internal bool ExecuteWorkbenchCommand(EditorCommand command)
    {
        SynchronizeNativeInput();
        return CanExecuteCommand(command) && ExecuteCommand(command, new(-1, -1, -1, -1), _completionGeneration);
    }

    private bool ExecuteCommand(EditorCommand command, BrowserSelection nativeSelection, int generation,
        EditorCommand? fallback = null, ModalInvocation? invocation = null)
    {
        if (command.Id == EditorCommandId.Escape) return ExecuteEscape(nativeSelection, invocation);
        if (command.Context == CommandContext.Editor && ExecuteModalCommand(command, nativeSelection, invocation)) return true;
        _modalPending = default;
        if (command.Context == CommandContext.Suggestions)
            return ExecuteCompletionCommand(command.Id, nativeSelection, generation, fallback);
        if (command.Context == CommandContext.Signatures || command.Id == EditorCommandId.SignatureHelp)
            return ExecuteSignatureCommand(command.Id, nativeSelection);

        return command.Id switch {
            EditorCommandId.Undo => ReplayHistory(redo: false),
            EditorCommandId.Redo => ReplayHistory(redo: true),
            EditorCommandId.Find or EditorCommandId.Replace or EditorCommandId.NextMatch or EditorCommandId.PreviousMatch
                => ExecuteFindCommand(command.Id, nativeSelection),
            EditorCommandId.GoToDefinition or EditorCommandId.FindReferences or EditorCommandId.RenameSymbol or EditorCommandId.FormatDocument
                => StartLanguageCommand(command, nativeSelection),
            EditorCommandId.Suggest => HandleManualCompletion(nativeSelection),
            _ => ExecuteStateCommand(command, nativeSelection),
        };
    }

    private bool ExecuteEscape(BrowserSelection selection, ModalInvocation? invocation)
    {
        var command = EditorCommand.Get(EditorCommandId.Escape);
        if (_keymap.Profile == KeyboardProfile.Vim) {
            _modalPending = default;
            CancelCompletion(); CancelSignature();
            if (_find.IsOpen) { _find.Close(); _view.SetFindMatch(false); }
            return ExecuteModalCommand(command, selection, invocation);
        }
        if (_modalPending != default) { _modalPending = default; return true; }
        if (_completionPending || _completion is not null) { CancelCompletion(); return true; }
        if (_signatureRequested || _signatureHelp is not null) { CancelSignature(); return true; }
        if (_find.IsOpen) { _find.Close(); _view.SetFindMatch(false); return true; }
        return ExecuteStateCommand(command, selection);
    }

    private bool ExecuteStateCommand(EditorCommand command, BrowserSelection nativeSelection)
    {
        if (command.Edit is not { } edit) return false;
        if ((_completionPending || _completion is not null) && command.Id is
            EditorCommandId.Indent or EditorCommandId.Outdent or EditorCommandId.Newline
            or EditorCommandId.Up or EditorCommandId.Down or EditorCommandId.SelectUp or EditorCommandId.SelectDown) {
            CancelCompletion();
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
            historyGroup: InputHistoryGroup(editBefore.Selection.Ranges.All(static range => range.Empty)
                ? command.Id switch { EditorCommandId.Backspace => EditGroup.Backward, EditorCommandId.Delete => EditGroup.Forward, _ => EditGroup.None }
                : EditGroup.None));
        ScheduleCompletion(before, after);
        return true;
    }

    private bool ExecuteModalCommand(EditorCommand command, BrowserSelection selection, ModalInvocation? invocation)
    {
        if (_keymap.Profile != KeyboardProfile.Vim) return false;
        if (InputMode == EditorInputMode.Insert && !command.Modal && command.Id != EditorCommandId.Escape) return false;
        var action = invocation ?? new ModalInvocation(command.Id);
        if (command.Id is EditorCommandId.Undo or EditorCommandId.Redo) {
            _modalPending = default;
            for (var index = 0; index < action.Count; index++) ReplayHistory(command.Id == EditorCommandId.Redo);
            var state = State.Value;
            Commit(state.Apply(new() { Selection = EditorSelection.Single(ModalEditing.Clamp(state.Doc, state.Selection.Main.Head)) }));
            ChangeInputMode(EditorInputMode.Normal);
            return true;
        }
        var before = State.Value;
        var selected = ApplyNativeSelection(before, selection);
        if (ModalEditing.Apply(selected, action, InputMode) is not { } edit) return false;
        CancelCompletion(); CancelSignature();
        if (InputMode != edit.Mode) EndHistoryGroup();
        ChangeInputMode(edit.Mode);
        _modalPending = default;
        Commit(edit.State, keyboardNavigation: ReferenceEquals(edit.State.Doc, before.Doc), historySelection: selected.Selection,
            historyGroup: InputHistoryGroup(EditGroup.None));
        return true;
    }
}
