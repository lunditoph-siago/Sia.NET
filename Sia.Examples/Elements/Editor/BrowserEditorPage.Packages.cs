using Sia;
using Sia.Examples.Dom;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly MetadataReferenceProvider _packageReferences;
    private readonly PackageRegistry _packages = new();
    private string? _packageSource;
    private DomElement? _packageDialog;
    private DomElement? _packageMessage;
    private BrowserPackagePanel? _packagePanel;
    private readonly List<DomElement> _packageElements = [];
    private bool _addingPackage;
    private int _packageDialogGeneration;

    private string PackageSource()
        => _workspace.TryGetEntity(PackageManifest.Path, out var file) && !EditorWorkspace.IsFolder(file)
            ? file.Get<WorkspaceFile>().Content : "[]";

    private async Task EnsureEditorPackagesAsync(CancellationToken cancellationToken)
    {
        if (_workspace.TryGetEntity(PackageManifest.Path, out var file)) {
            if (EditorWorkspace.IsFolder(file)) throw new InvalidDataException("packages.json must be a file.");
            await _workspace.EnsureContentLoadedAsync(file, cancellationToken);
        }
        var source = PackageSource();
        if (_packageSource != source) {
            _packages.Replace(PackageManifest.Read(source));
            _packageSource = source;
        }
        _packagePanel?.Synchronize(_packages.Snapshot);
        await _packages.EnsureAsync(_packageReferences, _lifetime.Token).WaitAsync(cancellationToken);
        if (_disposed) return;
        _packagePanel?.Synchronize(_packages.Snapshot);
        if (_packages.Snapshot.FirstOrDefault(static status => status.State == PackageLoadState.Failed) is { State: PackageLoadState.Failed } failure)
            throw new InvalidOperationException($"Package '{failure.Package.Id}': {failure.Error}");
    }

    private bool RoutePackages(string eventName, string argument)
    {
        switch (eventName) {
            case "editor-packages-open": OpenPackages(); return true;
            case "editor-packages-close": ClosePackages(); return true;
            case "editor-packages" when argument == $"{_packageDialogGeneration}:escape":
                ClosePackages(); return true;
            case "editor-packages" when argument == $"{_packageDialogGeneration}:accept":
                if (!_addingPackage) Track(AddEditorPackageAsync());
                return true;
            case "editor-packages-add":
                if (!_addingPackage) Track(AddEditorPackageAsync());
                return true;
            case "editor-packages-retry":
                _packages.Retry();
                Track(RefreshPackagesAsync());
                return true;
            case "editor-packages-edit": Track(EditPackagesAsync()); return true;
            default: return false;
        }
    }

    private void OpenPackages()
    {
        ClosePackages();
        DomElement Keep(DomElement element) { _packageElements.Add(element); return element; }
        _packageDialog = Keep(DomElement.Create("dialog").Class("editor-dialog")
            .Attr("aria-label", "Packages").Attr("data-editor-dialog", "editor-packages")
            .Attr("data-editor-dialog-stamp", (++_packageDialogGeneration).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var header = Keep(DomElement.Create("header").Class("editor-dialog-header"));
        header.Append(Keep(DomElement.Create("h2").Text("Packages")))
            .Append(Keep(DomElement.Create("button").Class("btn").Attr("type", "button")
                .Text("Close").On("click", "editor-packages-close")));
        _packageMessage = Keep(DomElement.Create("p").Class("editor-dialog-message").Attr("role", "status")
            .Text("Workspace references · packages.json. Declare dependencies explicitly."));
        _packagePanel = new(DomElement.Create("div"), "editor-packages-add");
        var content = Keep(DomElement.Create("div").Class("editor-dialog-list"));
        content.Append(_packageMessage).Append(_packagePanel.Element);
        _packageDialog.Append(header).Append(content);
        var footer = Keep(DomElement.Create("footer").Class("editor-dialog-footer"));
        foreach (var (text, action) in new[] { ("Edit packages.json", "editor-packages-edit"), ("Retry", "editor-packages-retry") })
            footer.Append(Keep(DomElement.Create("button").Class("btn").Attr("type", "button").Text(text).On("click", action)));
        _packageDialog.Append(footer);
        using var app = DomElement.Find("app");
        app.Append(_packageDialog);
        _packagePanel.Synchronize(_packages.Snapshot);
        Track(RefreshPackagesAsync());
    }

    private async Task RefreshPackagesAsync()
    {
        try {
            await EnsureEditorPackagesAsync(_lifetime.Token);
            _packageMessage?.Text("Workspace references · packages.json. Declare dependencies explicitly.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) _packageMessage?.Text(error.Message); }
        finally { if (!_disposed) DomRuntime.Flush(); }
    }

    private async Task AddEditorPackageAsync()
    {
        if (_packagePanel?.ReadAddition() is not { } package) return;
        _addingPackage = true;
        try {
            SaveEditorSources(synchronizeNativeInput: true);
            var declarations = PackageManifest.Read(PackageSource());
            var proposed = new PackageRegistry();
            proposed.Replace(declarations);
            proposed.Declare(package);
            var source = PackageManifest.Write(proposed.Snapshot.Select(static status => status.Package));
            var file = _workspace.TryGetEntity(PackageManifest.Path, out var existing) ? existing
                : await _workspace.CreateFileAsync("", PackageManifest.Path, _lifetime.Token, source);
            await _workspace.EnsureContentLoadedAsync(file, _lifetime.Token);
            foreach (var (group, current) in _editorEntities)
                if (current == file) _editors.RestoreSavedSource(group, source);
            _workspace.SetContent(file, source);
            await _workspace.SaveAsync(file, _lifetime.Token);
            if (_disposed) return;
            RenderExplorer();
            SyncDirtyIndicators();
            InvalidateLanguageContext();
            _packageSource = null;
            await RefreshPackagesAsync();
            CheckShaderSources();
            ScheduleLanguageDiagnostics(ActiveLanguageBinding(), immediate: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) _packageMessage?.Text(error.Message); }
        finally { _addingPackage = false; if (!_disposed) DomRuntime.Flush(); }
    }

    private async Task EditPackagesAsync()
    {
        ClosePackages();
        var file = _workspace.TryGetEntity(PackageManifest.Path, out var existing) ? existing
            : await _workspace.CreateFileAsync("", PackageManifest.Path, _lifetime.Token, "[]\n");
        if (_disposed) return;
        if (file.Get<WorkspaceFile>().Content.Length == 0) _workspace.SetContent(file, "[]\n");
        RenderExplorer();
        await OpenEntryAsync(file, pin: true);
    }

    private void ClosePackages()
    {
        _packageDialog?.Remove();
        _packagePanel?.Dispose();
        _packagePanel = null;
        for (var index = _packageElements.Count - 1; index >= 0; index--) _packageElements[index].Dispose();
        _packageElements.Clear();
        _packageDialog = null;
        _packageMessage = null;
    }
}
