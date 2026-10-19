using Sia;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private EditorFilePicker _filePicker = null!;

    private IReadOnlyList<EditorFileChoice> CaptureFileChoices()
        => [.. _workspace.RootEntries.SelectMany(EnumerateFilesUnder).Concat(_workspace.OpenFiles())
            .Where(static entity => entity.IsValid && !EditorWorkspace.IsFolder(entity)).Distinct()
            .Select(entity => new EditorFileChoice(KeyFor(entity), entity.Get<WorkspaceEntry>().Name, EditorWorkspace.PathOf(entity)))];

    private void InitializeFilePicker()
    {
        _filePicker = new("editor-file-picker", CaptureFileChoices, choice => Track(OpenPickedFileAsync(choice)));
    }

    private async Task OpenPickedFileAsync(EditorFileChoice choice)
    {
        if (!TryResolve(choice.Id, out var entity) || !entity.IsValid || EditorWorkspace.PathOf(entity) != choice.Path) return;
        await OpenEntryAsync(entity, pin: true);
        if (!_disposed && _activeEntity == entity && ActiveLanguageGroup(entity) is { } group) _editors.Focus(group);
    }

    private void OpenFilePicker(string binding)
    {
        var origin = _editors.TryResolveCellId(binding, out var group) ? group
            : ActiveLanguageBinding() is { } active && _editors.TryResolveCellId(active, out var current) ? current : null;
        _filePicker.Open(() => { if (origin is not null) _editors.Focus(origin); });
    }
}
