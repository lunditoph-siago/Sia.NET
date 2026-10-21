using Sia.Examples.Notebook;
using Sia;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    public bool Route(string payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var separator = payload.IndexOf(':');
        var eventName = separator < 0 ? payload : payload[..separator];
        var argument = separator < 0 ? string.Empty : payload[(separator + 1)..];
        if (RouteShader(eventName)) return true;
        if (RouteLanguageTools(eventName, argument)) return true;

        switch (eventName) {
            case "editor-page-input-tools":
                SetInputLayout(tools: !_inputToolsOpen, compact: _compactInput);
                return true;
            case "editor-page-input-reset":
                SetInputLayout(tools: false, compact: false);
                _root.Attr("data-editor-input-reset", (++_inputResetGeneration).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return true;
            case "editor-page-compact-input":
                var compact = !_compactInput;
                SetInputLayout(tools: compact, compact: compact);
                return true;
            case "editor-page-input":
                var input = argument.Split(':');
                if (input.Length == 2 && _editors.TryResolveCellId(input[0], out _)) {
                    if (input[1] == "keyboard-open") SetInputLayout(tools: true, compact: _compactInput);
                    else if (input[1] is "keyboard-close" or "dismiss") SetInputLayout(tools: false, compact: false);
                }
                return true;
            case "editor-page-tab":
                ActivateTab(argument);
                return true;
            case "editor-page-tab-close":
                Track(CloseTabAsync(argument));
                return true;
            case "editor-page-close-active-tab":
                CloseActiveTab();
                return true;
            case "editor-page-open":
                OpenPanelWindow(argument);
                return true;
            case "editor-page-toggle-output":
                ToggleOutputPane();
                return true;
            case "editor-page-clear":
                ClearOutput();
                return true;
            case "editor-page-save":
                Track(SaveActiveAsync());
                return true;
            case "editor-page-save-all":
                Track(SaveAllAsync());
                return true;
            case "editor-page-save-as-begin":
                BeginSaveAs();
                return true;
            case "editor-page-save-as-commit":
                Track(CommitSaveAsAsync(argument));
                return true;
            case "editor-page-save-as-cancel":
                CancelDialog(argument, restoreEditor: true);
                return true;
            case "editor-page-revert":
                RevertActiveFile();
                return true;
            case "editor-page-new-file":
                Track(CreateUntitledAsync());
                return true;
            case "editor-page-home":
                return true;
            case "editor-page-tree-open":
                Track(OpenEntryAsync(argument, pin: false));
                return true;
            case "editor-page-tree-pin":
                Track(OpenEntryAsync(argument, pin: true));
                return true;
            case "editor-page-tree-new-file":
                Track(CreateEntryAsync(argument, folder: false));
                return true;
            case "editor-page-tree-new-folder":
                Track(CreateEntryAsync(argument, folder: true));
                return true;
            case "editor-page-rename-entry":
                Track(RenameEntryAsync(argument));
                return true;
            case "editor-page-tree-delete":
                BeginDelete(argument);
                return true;
            case "editor-page-dialog-save":
                ResolveCloseDialog(CloseDecision.Save, argument);
                return true;
            case "editor-page-dialog-dont-save":
                ResolveCloseDialog(CloseDecision.DontSave, argument);
                return true;
            case "editor-page-dialog-cancel":
                CancelDialog(argument);
                return true;
            case "editor-page-dialog-delete-confirm":
                Track(ConfirmDeleteAsync(argument));
                return true;
            case "editor-page-dialog-delete-cancel":
                CancelDialog(argument);
                return true;
            case "cell":
                CellMove(argument);
                return true;
            case "cell-detach":
                CellDetach(argument);
                return true;
            case "cell-resize":
                CellResize(argument);
                return true;
            case "cell-normalize":
                CellNormalize(argument);
                return true;
            case "editor-focus":
                if (!_editors.Activate(argument)) return false;
                if (!_editors.TryResolveCellId(argument, out var groupId)
                    || !_editorEntities.TryGetValue(groupId, out var focused)
                    || !focused.IsValid || !focused.Contains<OpenEditorFile>()) {
                    return false;
                }
                if (_activeEntity != focused) {
                    ++_activationGeneration;
                    _activeEntity = focused;
                    UpdateChrome();
                    RefreshExplorerSelection();
                }
                UpdateCursorStatus();
                return true;
            default: {
                    var handled = _editors.Route(payload);
                    if (handled && eventName is "input" or "text" or "paste" or "mut" or "key" or "complete") {
                        SyncDirtyIndicators();
                        CheckShaderSources();
                    }
                    if (handled && eventName is "input" or "text" or "paste" or "mut" or "key" or "sel" or "complete" or "find")
                        UpdateCursorStatus();
                    return handled;
                }
        }
    }

    private void SetInputLayout(bool tools, bool compact)
    {
        if (_inputToolsOpen != tools) {
            _inputToolsOpen = tools;
            _root.ToggleClass("input-tools-open", tools);
            _inputToolsAction.Attr("aria-checked", tools ? "true" : "false");
        }
        if (_compactInput != compact) {
            _compactInput = compact;
            _root.ToggleClass("input-compact", compact);
            _compactInputAction.Attr("aria-checked", compact ? "true" : "false");
        }
    }

    internal bool CanExecuteCommand(EditorCommand command, string binding)
        => command.Id == EditorCommandId.QuickOpen ? _initialized && !_disposed
            : command.Id is EditorCommandId.DocumentSymbols or EditorCommandId.WorkspaceSymbols
                ? _initialized && !_disposed && ActiveLanguageBinding() is not null : command.Context == CommandContext.EditorLab ? command.Id switch {
                    EditorCommandId.NavigateBack => _navigationBack.Any(static bookmark => bookmark.Entity.IsValid),
                    EditorCommandId.NavigateForward => _navigationForward.Any(static bookmark => bookmark.Entity.IsValid),
                    EditorCommandId.CheckCode => ActiveLanguageBinding() is not null,
                    _ => true,
                } : _editors.CanExecuteCommand(ResolveCommandBinding(binding), command);

    internal string ResolveCommandBinding(string binding) => binding.Length == 0 ? ActiveLanguageBinding() ?? "" : binding;

    internal bool ExecuteCommand(EditorCommand command, string binding)
    {
        binding = ResolveCommandBinding(binding);
        if (command.Id == EditorCommandId.QuickOpen) {
            if (!_initialized || _disposed) return false;
            OpenFilePicker(binding); return true;
        }
        if (command.Id is EditorCommandId.DocumentSymbols or EditorCommandId.WorkspaceSymbols)
            return _initialized && !_disposed && OpenSymbolPicker(binding, command.Id == EditorCommandId.WorkspaceSymbols);
        if (!command.IsEditor) return command is { Context: CommandContext.EditorLab, Event: { } action } && Route(action);
        if (!_editors.ExecuteCommand(binding, command)) return false;
        SyncDirtyIndicators();
        CheckShaderSources();
        UpdateCursorStatus();
        return true;
    }

    private void RevertActiveFile()
    {
        if (_activeEntity is not { } entity || !entity.IsValid || entity.Contains<UntitledFile>()) {
            return;
        }
        var saved = entity.Get<WorkspaceFile>().SavedContent;
        SaveEditorSources(synchronizeNativeInput: true);
        foreach (var (group, current) in _editorEntities) {
            if (current == entity) _editors.RestoreSavedSource(group, saved);
        }
        _workspace.SetContent(entity, saved);
        _workspace.Pin(entity);
        SyncDirtyIndicators();
        CheckShaderSources();
        UpdateChrome("reverted to the saved version");
    }

    private Task CreateUntitledAsync()
    {
        ++_activationGeneration;
        var entity = _workspace.CreateUntitled();
        EnsureFileWindow(entity);
        _activeEntity = entity;
        UpdateState(state => NotebookCellLayout.OpenTabIntoHome(state, TabIdFor(entity)));
        if (ActiveLanguageGroup(entity) is { } group) _editors.Focus(group);
        return Task.CompletedTask;
    }

    private void ClearOutput()
    {
        ResetDiagnosticStatus();
        _operationError.Text(string.Empty);
        ShowCompilationDetails(false);
        ClearDiagnostics();
        _compilationOutput.Text(string.Empty).ToggleClass("output-error", false);
        _programConsole?.Clear();
        _programOutput.Text(string.Empty).ToggleClass("output-error", false);
        StopRendering();
    }
}
