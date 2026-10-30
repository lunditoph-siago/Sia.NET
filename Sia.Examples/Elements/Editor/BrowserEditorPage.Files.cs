namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private EditorNavigationPicker _navigationPicker = null!;

    private IReadOnlyList<EditorNavigationChoice> CaptureFileChoices()
        => [.._workspace.RootEntries.SelectMany(EnumerateFilesUnder).Concat(_workspace.OpenFiles())
            .Where(static entity => entity.IsValid && !EditorWorkspace.IsFolder(entity)).Distinct()
            .Select(entity => new EditorNavigationChoice(KeyFor(entity), entity.Get<WorkspaceEntry>().Name, EditorWorkspace.PathOf(entity)))];

    private void InitializeNavigationPicker()
    {
        _navigationPicker = new("editor-navigation-picker", CaptureFileChoices, choice => Track(OpenPickedFileAsync(choice)));
    }

    private async Task OpenPickedFileAsync(EditorNavigationChoice choice)
    {
        if (!TryResolve(choice.Id, out var entity) || !entity.IsValid || EditorWorkspace.PathOf(entity) != choice.Path) return;
        await OpenEntryAsync(entity, pin: true);
        if (!_disposed && _activeEntity == entity && ActiveLanguageGroup(entity) is { } group) _editors.Focus(group);
    }

    private void OpenFilePicker(string binding)
    {
        var origin = _editors.TryResolveCellId(binding, out var group) ? group
            : ActiveLanguageBinding() is { } active && _editors.TryResolveCellId(active, out var current) ? current : null;
        _navigationPicker.Open(() => { if (origin is not null) _editors.Focus(origin); });
    }

    private bool OpenSymbolPicker(string binding, bool workspace)
    {
        if (!_editors.TryResolveCellId(binding, out _) && ActiveLanguageBinding() is { } active) binding = active;
        if (!_editors.TryResolveCellId(binding, out var origin)) return false;
        Track(_navigationPicker.OpenSymbolsAsync(workspace, token => _editors.QuerySymbolsAsync(binding, workspace, token),
            target => !_disposed && _workspace.TryGetEntity(target.DocumentId, out var file) && file.IsValid
                && ReadLanguageSource(file, synchronize: true) == target.Source,
            target => Track(NavigateToSourceAsync(target, binding)), () => _editors.Focus(origin)));
        return true;
    }
}
