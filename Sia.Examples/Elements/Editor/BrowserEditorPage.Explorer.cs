using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage
{
    private readonly DomElement _explorerRoot = DomElement.Find("notebook-files");
    private readonly List<DomElement> _explorerElements = [];
    private readonly Dictionary<string, DomElement> _explorerFileButtons = [];

    private void RenderExplorer()
    {
        _explorerRoot.Text(string.Empty);
        foreach (var element in _explorerElements) {
            element.Dispose();
        }
        _explorerElements.Clear();
        _explorerFileButtons.Clear();

        var tools = Explorer(DomElement.Create("div").Class("file-tree-tools")
            .Attr("role", "toolbar").Attr("aria-label", "Workspace files"));
        tools.Append(Explorer(DomElement.Create("span").Text("Workspace")));
        AppendExplorerTool(tools, "New file in workspace", "editor-page-tree-new-file:",
            "M13 3H5v18h14V9Zm0 0v6h6M8 14h8m-4-4v8");
        AppendExplorerTool(tools, "New folder in workspace", "editor-page-tree-new-folder:",
            "M3 7V5h7l2 2h9v13H3Zm5 7h8m-4-4v8");
        var close = Explorer(DomElement.Create("button").Class("icon-btn").Class("sidebar-dismiss")
            .Attr("type", "button").Attr("data-sidebar-dismiss", string.Empty)
            .Attr("aria-label", "Close sidebar").Attr("title", "Close sidebar"));
        AppendExplorerIcon(close, "m6 6 12 12M18 6 6 18");
        tools.Append(close);
        _explorerRoot.Append(tools);

        var roots = _workspace.RootEntries;
        if (roots.Count == 0) {
            using var placeholder = DomElement.Create("span")
                .Class("file-tree-placeholder")
                .Text("This workspace has no files yet.");
            _explorerRoot.Append(placeholder);
            return;
        }
        foreach (var entry in roots) {
            AppendEntry(_explorerRoot, entry);
        }
        RefreshExplorerSelection();
    }

    private void AppendEntry(DomElement parent, Entity entry)
    {
        if (EditorWorkspace.IsFolder(entry)) {
            AppendFolder(parent, entry);
        } else {
            AppendFile(parent, entry);
        }
    }

    private void AppendFolder(DomElement parent, Entity folder)
    {
        var key = KeyFor(folder);
        var name = EditorWorkspace.NameOf(folder);
        var entry = Explorer(DomElement.Create("div").Class("file-tree-folder-entry")
            .Attr("data-file-tree-entry", key));
        var node = Explorer(DomElement.Create("details")
            .Class("file-tree-folder")
            .Attr("data-file-tree-folder", key)
            .Attr("open", string.Empty));
        var summary = Explorer(DomElement.Create("summary").Class("file-tree-folder-label"));
        var icon = Explorer(DomElement.Create("span")
            .Class("file-tree-folder-icon")
            .Attr("aria-hidden", "true")
            .Text("›"));
        var label = Explorer(DomElement.Create("span").Class("file-tree-folder-name")
            .Attr("title", EditorWorkspace.PathOf(folder)).Text(name));
        summary.Append(icon).Append(label);
        node.Append(summary);

        var children = Explorer(DomElement.Create("div").Class("file-tree-children"));
        foreach (var child in _workspace.ChildrenOf(folder)) {
            AppendEntry(children, child);
        }
        node.Append(children);
        entry.Append(node);
        var menu = AppendEntryMenu(entry, name);
        AppendEntryAction(menu, "New file", $"New file in {name}", $"editor-page-tree-new-file:{key}");
        AppendEntryAction(menu, "New folder", $"New folder in {name}", $"editor-page-tree-new-folder:{key}");
        AppendEntryAction(menu, "Rename…", $"Rename {name}", string.Empty, renameTriggerKey: key);
        AppendEntryAction(menu, "Delete…", $"Delete {name}", $"editor-page-tree-delete:{key}");
        AppendRenameInput(entry, key, name, keepExtension: true);
        parent.Append(entry);
    }

    private void AppendFile(DomElement parent, Entity file)
    {
        var key = KeyFor(file);
        var name = EditorWorkspace.NameOf(file);
        var (baseName, extension) = SplitExtension(name);
        var row = Explorer(DomElement.Create("div")
            .Class("file-tree-file")
            .Attr("data-file-tree-entry", key));
        var open = Explorer(DomElement.Create("button")
            .Class("file-tree-file-open")
            .Attr("type", "button")
            .Attr("data-file-tree-open", TabIdFor(file))
            .Attr("aria-label", $"Open {name}")
            .Attr("title", EditorWorkspace.PathOf(file))
            .On("click", $"editor-page-tree-open:{key}")
            .On("dblclick", $"editor-page-tree-pin:{key}"));
        var icon = Explorer(DomElement.Create("span")
            .Class("file-tree-file-icon")
            .Attr("aria-hidden", "true")
            .Text(extension.Length > 0 ? extension.TrimStart('.')[..1].ToUpperInvariant() : "•"));
        var nameLabel = Explorer(DomElement.Create("span").Class("file-tree-file-name").Text(baseName));
        open.Append(icon).Append(nameLabel);
        if (extension.Length > 0) {
            var extensionLabel = Explorer(
                DomElement.Create("span").Class("file-tree-file-extension").Text(extension));
            open.Append(extensionLabel);
        }
        row.Append(open);
        var menu = AppendEntryMenu(row, name);
        AppendEntryAction(menu, "Rename…", $"Rename {name}", string.Empty, renameTriggerKey: key);
        AppendEntryAction(menu, "Delete…", $"Delete {name}", $"editor-page-tree-delete:{key}");
        AppendRenameInput(row, key, name, keepExtension: true);
        parent.Append(row);
        _explorerFileButtons[key] = open;
    }

    private DomElement AppendEntryMenu(DomElement parent, string name)
    {
        var host = Explorer(DomElement.Create("details").Class("menu-toggle").Class("file-tree-menu"));
        var summary = Explorer(DomElement.Create("summary").Class("icon-btn")
            .Attr("aria-label", $"Actions for {name}").Attr("title", $"Actions for {name}"));
        AppendExplorerIcon(summary, "M5 12h.01M12 12h.01M19 12h.01");
        var menu = Explorer(DomElement.Create("div").Class("menu-popover").Class("file-tree-actions"));
        parent.Append(host.Append(summary).Append(menu));
        return menu;
    }

    private void AppendExplorerTool(DomElement parent, string label, string payload, string path)
    {
        var button = Explorer(DomElement.Create("button").Class("icon-btn")
            .Attr("type", "button").Attr("aria-label", label).Attr("title", label).On("click", payload));
        AppendExplorerIcon(button, path);
        parent.Append(button);
    }

    private void AppendExplorerIcon(DomElement parent, string path)
    {
        var svg = Explorer(DomElement.Create("svg").Attr("viewBox", "0 0 24 24")
            .Attr("width", "18").Attr("height", "18").Attr("fill", "none")
            .Attr("stroke", "currentColor").Attr("stroke-width", "1.5")
            .Attr("stroke-linecap", "round").Attr("stroke-linejoin", "round").Attr("aria-hidden", "true"));
        svg.Append(Explorer(DomElement.Create("path").Attr("d", path)));
        parent.Append(svg);
    }

    private void AppendEntryAction(
        DomElement parent,
        string text,
        string label,
        string payload,
        string? renameTriggerKey = null)
    {
        var button = Explorer(DomElement.Create("button")
            .Class("menu-item")
            .Attr("type", "button")
            .Attr("aria-label", label)
            .Attr("title", label)
            .Text(text));
        if (renameTriggerKey is { } key) {
            button.Attr("data-file-tree-rename-trigger", key);
        }
        if (payload.Length > 0) {
            button.On("click", payload);
        }
        parent.Append(button);
    }

    private void AppendRenameInput(DomElement parent, string key, string name, bool keepExtension)
    {
        var input = Explorer(DomElement.Create("input")
            .Class("file-tree-rename-input")
            .Attr("type", "text")
            .Attr("value", name)
            .Attr("data-file-tree-rename", key)
            .Attr("data-file-tree-rename-event", "editor-page-rename-entry")
            .Attr("data-saved-value", name)
            .Attr("aria-label", $"Rename {name}")
            .Attr("autocorrect", "off")
            .Attr("autocapitalize", "off")
            .Attr("spellcheck", "false")
            .Attr("enterkeyhint", "done"));
        if (keepExtension) {
            input.Attr("data-file-tree-rename-keep-extension", "true");
        }
        parent.Append(input);
    }

    private void RefreshExplorerSelection()
    {
        var activeKey = _activeEntity is { } entity && entity.IsValid ? KeyFor(entity) : null;
        foreach (var (key, button) in _explorerFileButtons) {
            var active = key == activeKey;
            button.ToggleClass("active", active).Attr("aria-current", active ? "true" : "false");
        }
    }

    private async Task RenameEntryAsync(string arguments)
    {
        var separator = arguments.IndexOf(':');
        if (separator < 0) {
            return;
        }
        var key = arguments[..separator];
        var name = Uri.UnescapeDataString(arguments[(separator + 1)..]).Trim();
        if (name.Length == 0 || !TryResolve(key, out var entity) || !entity.IsValid) {
            return;
        }
        await _workspace.RenameAsync(entity, name, _lifetime.Token);
        if (_disposed || !entity.IsValid) {
            return;
        }
        ApplyLayout();
        RenderExplorer();
        UpdateChrome();
    }

    private async Task CreateEntryAsync(string folderKey, bool folder)
    {
        string folderPath;
        if (folderKey.Length == 0) {
            folderPath = string.Empty;
        } else if (TryResolve(folderKey, out var parent) && parent.IsValid) {
            folderPath = EditorWorkspace.PathOf(parent);
        } else {
            return;
        }
        var activation = folder ? _activationGeneration : ++_activationGeneration;
        var name = ReserveDefaultName(folderPath, folder);
        var created = folder
            ? await _workspace.CreateFolderAsync(folderPath, name, _lifetime.Token)
            : await _workspace.CreateFileAsync(folderPath, name, _lifetime.Token);
        if (_disposed) {
            return;
        }
        RenderExplorer();
        if (!folder && created.IsValid && activation == _activationGeneration) {
            await OpenEntryAsync(created, pin: true);
        }
    }

    private string ReserveDefaultName(string folderPath, bool folder)
    {
        var stem = folder ? "New Folder" : "Untitled";
        var extension = folder ? string.Empty : ".cs";
        var candidate = stem + extension;
        for (var attempt = 2; _workspace.TryGetEntity(WorkspacePath.Combine(folderPath, candidate), out _); attempt++) {
            candidate = $"{stem}-{attempt}{extension}";
        }
        return candidate;
    }

    private void DisposeExplorer()
    {
        _explorerRoot.Text(string.Empty);
        foreach (var element in _explorerElements) {
            element.Dispose();
        }
        _explorerElements.Clear();
        _explorerFileButtons.Clear();
        using var placeholder = DomElement.Create("span")
            .Class("file-tree-placeholder")
            .Text("Open a notebook to see its files.");
        _explorerRoot.Append(placeholder);
        _explorerRoot.Dispose();
    }

    private static (string BaseName, string Extension) SplitExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? (name[..dot], name[dot..]) : (name, string.Empty);
    }

    private DomElement Explorer(DomElement element)
    {
        _explorerElements.Add(element);
        return element;
    }

    private void ActivateWorkspaceSidebar()
    {
        using var sidebar = DomElement.TryFind("sidebar");
        sidebar?.Attr("data-sidebar-view", "workspace");
        SetSidebarTab("sidebar-explorer-tab", active: false);
        SetSidebarTab("sidebar-files-tab", active: false);
        SetSidebarPanel("sidebar-explorer", hidden: true);
        SetSidebarPanel("sidebar-files", hidden: false);
        using var files = DomElement.TryFind("sidebar-files");
        files?.Attr("role", "region").Attr("aria-label", "Workspace files").Attr("aria-labelledby", string.Empty);
    }

    private void RestoreExampleSidebar()
    {
        using var sidebar = DomElement.TryFind("sidebar");
        sidebar?.Attr("data-sidebar-view", "explorer");
        SetSidebarTab("sidebar-explorer-tab", active: true);
        SetSidebarTab("sidebar-files-tab", active: false);
        SetSidebarPanel("sidebar-explorer", hidden: false);
        SetSidebarPanel("sidebar-files", hidden: true);
        using var files = DomElement.TryFind("sidebar-files");
        files?.Attr("role", "tabpanel").Attr("aria-label", string.Empty).Attr("aria-labelledby", "sidebar-files-tab");
    }

    private static void SetSidebarTab(string id, bool active)
    {
        using var tab = DomElement.TryFind(id);
        tab?.ToggleClass("active", active)
            .Attr("aria-selected", active ? "true" : "false")
            .Attr("tabindex", active ? "0" : "-1");
    }

    private static void SetSidebarPanel(string id, bool hidden)
    {
        using var panel = DomElement.TryFind(id);
        panel?.ToggleClass("hidden", hidden);
    }
}
