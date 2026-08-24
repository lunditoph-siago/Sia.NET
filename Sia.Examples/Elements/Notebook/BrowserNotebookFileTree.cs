using Sia_Examples.Dom;

namespace Sia_Examples.Notebook;

internal sealed class BrowserNotebookFileTree : IDisposable
{
    private readonly DomElement _root = DomElement.Find("notebook-files");
    private readonly Dictionary<string, FileEntry> _files = [];
    private bool _disposed;

    public BrowserNotebookFileTree(NotebookDocument document, NotebookCellState cellState)
        => Update(document, cellState);

    public void Update(NotebookDocument document, NotebookCellState cellState)
    {
        ThrowIfDisposed();
        Clear();

        var cellIndex = 0;
        foreach (var section in document.Sections) {
            var cells = section.Blocks.OfType<CodeCellBlock>().ToArray();
            if (cells.Length == 0) {
                continue;
            }

            using var sectionNode = DomElement.Create("details")
                .Class("file-tree-folder")
                .Attr("data-file-tree-folder", $"section:{section.Id}")
                .Attr("open", string.Empty);
            using var sectionSummary = DomElement.Create("summary")
                .Class("file-tree-folder-label");
            AppendFolderLabel(sectionSummary, section.Title, "section");
            sectionNode.Append(sectionSummary);

            using var sectionChildren = DomElement.Create("div").Class("file-tree-children");
            foreach (var cell in cells) {
                cellIndex++;
                using var cellNode = DomElement.Create("details")
                    .Class("file-tree-folder")
                    .Attr("data-file-tree-folder", $"cell:{cell.Id}")
                    .Attr("open", string.Empty);
                using var cellSummary = DomElement.Create("summary")
                    .Class("file-tree-folder-label");
                AppendFolderLabel(cellSummary, $"Cell {cellIndex}", "cell");
                if (!string.IsNullOrWhiteSpace(cell.Scope)) {
                    using var scope = DomElement.Create("span")
                        .Class("file-tree-scope")
                        .Attr("title", $"Scope: {cell.Scope}")
                        .Text(cell.Scope);
                    cellSummary.Append(scope);
                }
                cellNode.Append(cellSummary);

                using var cellChildren = DomElement.Create("div").Class("file-tree-children");
                foreach (var script in cell.Scripts) {
                    AppendScript(cellChildren, script, cellState);
                }
                cellNode.Append(cellChildren);
                sectionChildren.Append(cellNode);
            }
            sectionNode.Append(sectionChildren);
            _root.Append(sectionNode);
        }

        if (_files.Count == 0) {
            using var empty = DomElement.Create("span")
                .Class("file-tree-placeholder")
                .Text("This notebook has no script files.");
            _root.Append(empty);
        }
        UpdateSelection(cellState);
    }

    public void UpdateSelection(NotebookCellState cellState)
    {
        ThrowIfDisposed();
        var activeTabs = NotebookCellLayout.EnumerateGroups(cellState)
            .Select(static group => group.ActiveTabId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var entry in _files.Values) {
            var active = activeTabs.Contains(entry.TabId);
            entry.Button
                .ToggleClass("active", active)
                .Attr("aria-current", active ? "true" : "false");
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        Clear();
        using var placeholder = DomElement.Create("span")
            .Class("file-tree-placeholder")
            .Text("Open a notebook to see its files.");
        _root.Append(placeholder);
        _root.Dispose();
        _disposed = true;
    }

    private void AppendScript(
        DomElement parent,
        CellScript script,
        NotebookCellState cellState)
    {
        var window = cellState.GetScriptWindow(script.Id);
        var tab = cellState.GetTabForWindow(window.Id);
        using var row = DomElement.Create("div")
            .Class("file-tree-file")
            .Attr("data-file-tree-entry", script.Id);
        var open = DomElement.Create("button")
            .Class("file-tree-file-open")
            .Attr("type", "button")
            .Attr("data-file-tree-open", tab.Id)
            .Attr("aria-label", $"Open {window.Title}.cs")
            .Attr("title", $"Open {window.Title}.cs")
            .On("click", $"activate-tab:{tab.Id}");
        using (var icon = DomElement.Create("span")
            .Class("file-tree-file-icon")
            .Attr("aria-hidden", "true")
            .Text("#")) {
            open.Append(icon);
        }
        using (var name = DomElement.Create("span")
            .Class("file-tree-file-name")
            .Text(window.Title)) {
            open.Append(name);
        }
        using (var extension = DomElement.Create("span")
            .Class("file-tree-file-extension")
            .Text(".cs")) {
            open.Append(extension);
        }
        using var rename = DomElement.Create("button")
            .Class("file-tree-file-action")
            .Attr("type", "button")
            .Attr("data-file-tree-rename-trigger", script.Id)
            .Attr("aria-label", $"Rename {window.Title}.cs")
            .Attr("title", "Rename file")
            .Text("✎");
        using var input = DomElement.Create("input")
            .Class("file-tree-rename-input")
            .Attr("type", "text")
            .Attr("value", window.Title)
            .Attr("data-file-tree-rename", script.Id)
            .Attr("data-saved-value", window.Title)
            .Attr("aria-label", $"Rename {window.Title}.cs")
            .Attr("autocorrect", "off")
            .Attr("autocapitalize", "off")
            .Attr("spellcheck", "false")
            .Attr("enterkeyhint", "done");
        row.Append(open).Append(rename).Append(input);
        parent.Append(row);
        _files.Add(script.Id, new(open, tab.Id));
    }

    private static void AppendFolderLabel(DomElement parent, string title, string kind)
    {
        using var icon = DomElement.Create("span")
            .Class("file-tree-folder-icon")
            .Attr("aria-hidden", "true")
            .Text("›");
        using var name = DomElement.Create("span")
            .Class("file-tree-folder-name")
            .Text(string.IsNullOrWhiteSpace(title) ? $"Untitled {kind}" : title);
        parent.Append(icon).Append(name);
    }

    private void Clear()
    {
        _root.Text(string.Empty);
        foreach (var entry in _files.Values) {
            entry.Button.Dispose();
        }
        _files.Clear();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record FileEntry(DomElement Button, string TabId);
}
