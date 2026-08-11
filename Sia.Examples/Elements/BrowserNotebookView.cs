using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public sealed class BrowserNotebookView :
    INotebookView,
    IDisposable
{
    private readonly DomElement _container;
    private readonly DomElement _root;
    private readonly BrowserEditorRegistry _editors;
    private readonly BrowserPackagePanel _packages;
    private readonly BrowserDockWorkspaceView _dock;
    private readonly Dictionary<string, BrowserCellView> _cells = [];
    private bool _disposed;

    public BrowserNotebookView(
        World world,
        NotebookDocument document,
        ICompilationReferenceResolver references,
        NotebookDockState dockState)
    {
        _container = DomElement.Find("notebook");
        _root = DomElement.Create("div").Class("notebook-document");
        _editors = new(world, references);
        _packages = new();
        var floatingLayer = DomElement.Create("div")
            .Class("section")
            .Class("dock-floating-layer");
        _dock = new(floatingLayer);

        RenderTitleBar(document.Title);

        for (var index = 0; index < document.Sections.Count; index++) {
            RenderSection(document.Sections[index], index, dockState);
        }
        _root.Append(floatingLayer);

        _container.Text(string.Empty).Append(_root);
    }

    public BrowserEditorRegistry Editors => _editors;

    public void BeginEditorEdit(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var cell)) {
            cell.BeginEditing();
        }
    }

    public void UpdateEditorDirty(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var cell)) {
            cell.UpdateDirtyState();
        }
    }

    public void EndEditorEdit(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var cell)) {
            cell.EndEditing();
        }
    }

    public void DiscardEditor(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var cell)) {
            cell.DiscardChanges();
        }
    }

    void IRenderHost<NotebookCellSnapshot>.Upsert(in NotebookCellSnapshot view)
        => _cells[view.Id].Update(view.State);

    void IRenderHost<NotebookCellSnapshot>.Remove(in NotebookCellSnapshot view)
    {
    }

    void IRenderHost<PackageView>.Upsert(in PackageView view)
        => _packages.Upsert(view);

    void IRenderHost<PackageView>.Remove(in PackageView view)
        => _packages.Remove(view);

    void IRenderHost<PackageCountView>.Upsert(in PackageCountView view)
        => _packages.UpdateCount(view.Count);

    void IRenderHost<PackageCountView>.Remove(in PackageCountView view)
        => _packages.UpdateCount(0);

    void IRenderHost<NotebookDockPresentation>.Upsert(
        in NotebookDockPresentation view)
        => _dock.Apply(view.State);

    void IRenderHost<NotebookDockPresentation>.Remove(
        in NotebookDockPresentation view)
    {
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _dock.Dispose();
        _editors.Dispose();
        foreach (var cell in _cells.Values) {
            cell.Dispose();
        }
        _cells.Clear();
        _packages.Dispose();
        _root.Remove();
        _root.Dispose();
        _container.Dispose();
    }

    private void RenderTitleBar(string title)
    {
        using var bar = DomElement.Create("header").Class("notebook-titlebar");
        using var input = CreateInlineInput(
            "notebook-title-input",
            "Notebook title",
            title,
            "Untitled");
        using var actions = DomElement.Create("div").Class("inline-edit-actions");
        AppendInlineSaveButton(
            actions,
            "Save title",
            "notebook-title-input",
            "rename-title");
        AppendDiscardButton(actions, "notebook-title-input");
        using var documentActions = DomElement.Create("div").Class("title-actions");
        AppendAddMenu(
            documentActions,
            "New section",
            [("⌁", "Code section", "insert-section:code"), ("¶", "Documentation section", "insert-section:text")]);
        bar.Append(input).Append(actions).Append(documentActions);
        _root.Append(bar);
    }

    private void RenderSection(
        NotebookSection section,
        int sectionIndex,
        NotebookDockState dockState)
    {
        using var sectionElement = DomElement.Create("section")
            .Class("section")
            .Class("notebook-section");
        using var heading = DomElement.Create("header").Class("section-heading-editor");
        var inputId = $"section-title-input-{sectionIndex}";
        using var input = CreateInlineInput(
            inputId,
            $"Section {sectionIndex + 1} title",
            section.Title,
            "Untitled section");
        using var titleActions = DomElement.Create("div").Class("inline-edit-actions");
        AppendInlineSaveButton(
            titleActions,
            "Save section title",
            inputId,
            $"rename-section:{sectionIndex}");
        AppendDiscardButton(titleActions, inputId);
        using var sectionActions = DomElement.Create("div").Class("section-actions");
        var lastBlockId = section.Blocks.Select(GetBlockId).LastOrDefault(id => id is not null);
        if (lastBlockId is not null) {
            AppendAddMenu(
                sectionActions,
                "New cell",
                [("⌁", "Code cell", $"insert-cell:{lastBlockId}"), ("¶", "Text", $"insert-paragraph:{lastBlockId}")]);
        }
        AppendIconButton(
            sectionActions,
            "×",
            "Delete section",
            $"remove-section:{sectionIndex}");
        heading.Append(input).Append(titleActions).Append(sectionActions);
        sectionElement.Append(heading);

        foreach (var block in section.Blocks) {
            switch (block) {
                case ParagraphBlock { Editable: true } paragraph:
                    AppendEditableParagraph(sectionElement, paragraph);
                    break;
                case ParagraphBlock paragraph:
                    using (var element = DomElement.Create("p").Class("paragraph")) {
                        AppendInlines(element, paragraph.Inlines);
                        sectionElement.Append(element);
                    }
                    break;
                case ListBlock list:
                    using (var element = DomElement.Create("ul").Class("list")) {
                        foreach (var item in list.Items) {
                            using var listItem = DomElement.Create("li");
                            AppendInlines(listItem, item);
                            element.Append(listItem);
                        }
                        sectionElement.Append(element);
                    }
                    break;
                case CodeCellBlock cell:
                    var scriptWindow = dockState.GetWindow(
                        cell.Id,
                        DockWindowKind.Script);
                    var outputWindow = dockState.GetWindow(
                        cell.Id,
                        DockWindowKind.Output);
                    var renderWindow = dockState.GetWindow(
                        cell.Id,
                        DockWindowKind.Render);
                    var cellView = new BrowserCellView(
                        cell,
                        _editors,
                        scriptWindow,
                        outputWindow,
                        renderWindow);
                    _cells.Add(cell.Id, cellView);
                    _dock.RegisterWindow(cellView.Script);
                    _dock.RegisterWindow(cellView.Output);
                    _dock.RegisterWindow(cellView.Render);
                    var region = DomElement.Create("div");
                    _dock.RegisterRegion(scriptWindow.HomeRegionId, region);
                    sectionElement.Append(region);
                    break;
            }
        }
        _root.Append(sectionElement);
    }

    private static DomElement CreateInlineInput(
        string id,
        string label,
        string value,
        string placeholder)
        => DomElement.Create("input")
            .Class("inline-title-input")
            .Id(id)
            .Attr("type", "text")
            .Attr("aria-label", label)
            .Attr("value", value)
            .Attr("placeholder", placeholder)
            .Attr("data-inline-input", "true")
            .Attr("data-saved-value", value);

    private static void AppendIconButton(
        DomElement parent,
        string icon,
        string label,
        string payload)
    {
        using var button = DomElement.Create("button")
            .Class("icon-btn")
            .Attr("type", "button")
            .Attr("aria-label", label)
            .Attr("title", label)
            .On("click", payload)
            .Text(icon);
        parent.Append(button);
    }

    private static void AppendDiscardButton(DomElement parent, string inputId)
    {
        using var button = DomElement.Create("button")
            .Class("icon-btn")
            .Attr("type", "button")
            .Attr("aria-label", "Discard changes")
            .Attr("title", "Discard changes")
            .Attr("data-inline-discard", inputId)
            .Text("↶");
        parent.Append(button);
    }

    private static void AppendInlineSaveButton(
        DomElement parent,
        string label,
        string inputId,
        string payload)
    {
        using var button = DomElement.Create("button")
            .Class("icon-btn")
            .Attr("type", "button")
            .Attr("aria-label", label)
            .Attr("title", label)
            .Attr("data-inline-save", inputId)
            .On("click", payload)
            .Text("▣");
        parent.Append(button);
    }

    private static void AppendInlines(
        DomElement parent,
        IReadOnlyList<Inline> inlines)
    {
        foreach (var inline in inlines) {
            using var child = inline switch {
                TextInline text => DomElement.CreateText(text.Text),
                CodeInline code => DomElement.Create("code").Text(code.Text),
                _ => throw new InvalidOperationException(
                    $"Unsupported inline type '{inline.GetType().Name}'."),
            };
            parent.Append(child);
        }
    }

    private static void AppendEditableParagraph(DomElement parent, ParagraphBlock paragraph)
    {
        var elementId = NotebookElementIds.Paragraph(paragraph.Id);
        var text = FlattenInlines(paragraph.Inlines);
        using var wrapper = DomElement.Create("div").Class("paragraph-editor");
        using var textarea = DomElement.Create("textarea")
            .Class("inline-title-input")
            .Class("paragraph-textarea")
            .Id(elementId)
            .Attr("aria-label", "Paragraph text")
            .Attr("placeholder", "Write documentation…")
            .Attr("data-inline-input", "true")
            .Attr("data-saved-value", text)
            .Attr("data-allow-empty", "true")
            .Text(text);
        using var actions = DomElement.Create("div").Class("inline-edit-actions");
        AppendInlineSaveButton(actions, "Save paragraph", elementId, $"save-paragraph:{paragraph.Id}");
        AppendDiscardButton(actions, elementId);
        using var blockActions = DomElement.Create("div").Class("section-actions");
        AppendIconButton(blockActions, "↑", "Move up", $"move-cell-up:{paragraph.Id}");
        AppendIconButton(blockActions, "↓", "Move down", $"move-cell-down:{paragraph.Id}");
        AppendIconButton(blockActions, "×", "Delete", $"remove-cell:{paragraph.Id}");
        wrapper.Append(textarea).Append(actions).Append(blockActions);
        parent.Append(wrapper);
    }

    private static string FlattenInlines(IReadOnlyList<Inline> inlines)
        => string.Concat(inlines.Select(static inline => inline switch {
            TextInline text => text.Text,
            CodeInline code => code.Text,
            _ => "",
        })).Trim();

    private static void AppendAddMenu(
        DomElement parent,
        string label,
        IReadOnlyList<(string Icon, string Label, string Payload)> items)
    {
        using var host = DomElement.Create("details").Class("cell-more");
        using var summary = DomElement.Create("summary")
            .Class("icon-btn")
            .Attr("aria-label", label)
            .Attr("title", label)
            .Text("+");
        using var menu = DomElement.Create("div").Class("cell-more-menu");
        foreach (var (icon, itemLabel, payload) in items) {
            AppendMenuItem(menu, icon, itemLabel, payload);
        }
        host.Append(summary).Append(menu);
        parent.Append(host);
    }

    private static void AppendMenuItem(
        DomElement menu,
        string icon,
        string label,
        string payload)
    {
        using var button = DomElement.Create("button")
            .Class("cell-menu-item")
            .Attr("type", "button")
            .On("click", payload);
        using var glyph = DomElement.Create("span")
            .Class("cell-menu-icon")
            .Attr("aria-hidden", "true")
            .Text(icon);
        using var text = DomElement.Create("span").Text(label);
        button.Append(glyph).Append(text);
        menu.Append(button);
    }

    private static string? GetBlockId(NotebookBlock block)
        => block switch {
            CodeCellBlock cell => cell.Id,
            ParagraphBlock paragraph => paragraph.Id,
            _ => null,
        };
}
