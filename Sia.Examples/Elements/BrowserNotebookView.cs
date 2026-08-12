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
    private readonly DomElement _floatingLayer;
    private readonly BrowserEditorRegistry _editors;
    private readonly BrowserPackagePanel _packages;
    private readonly BrowserDockWorkspaceView _dock;
    private readonly Dictionary<string, BrowserCellView> _cells = [];
    private readonly Dictionary<string, DomElement> _sectionElements = [];
    private readonly Dictionary<string, DomElement> _sectionActionHosts = [];
    private readonly Dictionary<string, DomElement> _sectionTitleInputs = [];
    private readonly Dictionary<string, DomElement> _blockElements = [];
    private readonly Dictionary<string, List<string>> _sectionBlockOrder = [];
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
        _floatingLayer = DomElement.Create("div")
            .Class("section")
            .Class("dock-floating-layer");
        _dock = new(_floatingLayer);

        RenderTitleBar(document.Title);

        for (var index = 0; index < document.Sections.Count; index++) {
            var section = document.Sections[index];
            var sectionElement = CreateSectionShell(section, index);
            _sectionElements.Add(section.Id, sectionElement);
            _root.Append(sectionElement);
            ReconcileSectionBlocks(section.Id, [], section.Blocks, dockState, dockState);
        }
        _root.Append(_floatingLayer);

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

    public void ReconcileDocument(
        NotebookDocument previousDocument,
        NotebookDocument nextDocument,
        NotebookDockState previousDockState,
        NotebookDockState nextDockState)
    {
        var previousSections = previousDocument.Sections.ToDictionary(static section => section.Id);
        var nextSectionIds = new HashSet<string>(
            nextDocument.Sections.Select(static section => section.Id),
            StringComparer.Ordinal);
        var sectionsChanged = false;

        foreach (var (sectionId, section) in previousSections) {
            if (nextSectionIds.Contains(sectionId)) {
                continue;
            }
            sectionsChanged = true;
            ReconcileSectionBlocks(sectionId, section.Blocks, [], previousDockState, nextDockState);
            if (_sectionElements.Remove(sectionId, out var sectionElement)) {
                sectionElement.Remove();
                sectionElement.Dispose();
            }
            _sectionActionHosts.Remove(sectionId);
            _sectionTitleInputs.Remove(sectionId);
            _sectionBlockOrder.Remove(sectionId);
        }

        for (var sectionIndex = 0; sectionIndex < nextDocument.Sections.Count; sectionIndex++) {
            var section = nextDocument.Sections[sectionIndex];
            if (!_sectionElements.ContainsKey(section.Id)) {
                sectionsChanged = true;
                var sectionElement = CreateSectionShell(section, sectionIndex);
                _sectionElements.Add(section.Id, sectionElement);
                InsertSectionElement(sectionElement, sectionIndex, nextDocument);
            }

            var previousBlocks = previousSections.TryGetValue(section.Id, out var previousSection)
                ? previousSection.Blocks
                : [];
            ReconcileSectionBlocks(section.Id, previousBlocks, section.Blocks, previousDockState, nextDockState);
        }

        if (sectionsChanged) {
            for (var sectionIndex = 0; sectionIndex < nextDocument.Sections.Count; sectionIndex++) {
                var sectionId = nextDocument.Sections[sectionIndex].Id;
                _sectionTitleInputs[sectionId].Attr("aria-label", $"Section {sectionIndex + 1} title");
            }
        }
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

    private DomElement CreateSectionShell(NotebookSection section, int sectionIndex)
    {
        var sectionElement = DomElement.Create("section")
            .Class("section")
            .Class("notebook-section");
        using var heading = DomElement.Create("header").Class("section-heading-editor");
        var inputId = NotebookElementIds.SectionTitleInput(section.Id);
        var input = CreateInlineInput(
            inputId,
            $"Section {sectionIndex + 1} title",
            section.Title,
            "Untitled section");
        _sectionTitleInputs.Add(section.Id, input);
        using var titleActions = DomElement.Create("div").Class("inline-edit-actions");
        AppendInlineSaveButton(
            titleActions,
            "Save section title",
            inputId,
            $"rename-section:{section.Id}");
        AppendDiscardButton(titleActions, inputId);
        var sectionActions = DomElement.Create("div").Class("section-actions");
        _sectionActionHosts.Add(section.Id, sectionActions);
        RefreshSectionActions(section.Id, section.Blocks);
        heading.Append(input).Append(titleActions).Append(sectionActions);
        sectionElement.Append(heading);
        return sectionElement;
    }

    private void RefreshSectionActions(string sectionId, IReadOnlyList<NotebookBlock> blocks)
    {
        var sectionActions = _sectionActionHosts[sectionId];
        sectionActions.Text(string.Empty);
        var lastBlockId = blocks.Select(GetBlockId).LastOrDefault(id => id is not null);
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
            $"remove-section:{sectionId}");
    }

    private void InsertSectionElement(
        DomElement sectionElement, int sectionIndex, NotebookDocument nextDocument)
    {
        DomElement? before = null;
        for (var index = sectionIndex + 1; index < nextDocument.Sections.Count; index++) {
            if (_sectionElements.TryGetValue(nextDocument.Sections[index].Id, out var sibling)) {
                before = sibling;
                break;
            }
        }
        _root.InsertBefore(sectionElement, before ?? _floatingLayer);
    }

    private void ReconcileSectionBlocks(
        string sectionId,
        IReadOnlyList<NotebookBlock> previousBlocks,
        IReadOnlyList<NotebookBlock> nextBlocks,
        NotebookDockState previousDockState,
        NotebookDockState nextDockState)
    {
        var previousKeyed = KeyBlocks(previousBlocks);
        var nextKeyed = KeyBlocks(nextBlocks);
        var previousOrder = previousKeyed.Select(static entry => entry.Key).ToList();
        var nextOrder = nextKeyed.Select(static entry => entry.Key).ToList();

        if (previousOrder.SequenceEqual(nextOrder, StringComparer.Ordinal)) {
            return;
        }

        var nextKeys = new HashSet<string>(nextOrder, StringComparer.Ordinal);
        foreach (var (key, block) in previousKeyed) {
            if (!nextKeys.Contains(key)) {
                RemoveBlockElement(key, block, previousDockState);
            }
        }

        var previousKeys = new HashSet<string>(previousOrder, StringComparer.Ordinal);
        foreach (var (key, block) in nextKeyed) {
            if (!previousKeys.Contains(key)) {
                _blockElements.Add(key, CreateBlockElement(block, nextDockState));
            }
        }

        if (_sectionElements.TryGetValue(sectionId, out var sectionElement)) {
            var trackedOrder = _sectionBlockOrder.TryGetValue(sectionId, out var tracked) ? tracked : [];
            RepositionBlocks(sectionElement, trackedOrder, nextOrder);
        }
        _sectionBlockOrder[sectionId] = nextOrder;

        RefreshSectionActions(sectionId, nextBlocks);
    }

    private void RepositionBlocks(
        DomElement sectionElement, IReadOnlyList<string> previousOrder, IReadOnlyList<string> nextOrder)
    {
        var previousSet = new HashSet<string>(previousOrder, StringComparer.Ordinal);
        var nextSet = new HashSet<string>(nextOrder, StringComparer.Ordinal);
        var previousSurvivors = previousOrder.Where(nextSet.Contains).ToList();
        var nextSurvivors = nextOrder.Where(previousSet.Contains).ToList();
        var alreadyPlaced = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < previousSurvivors.Count && index < nextSurvivors.Count; index++) {
            if (previousSurvivors[index] == nextSurvivors[index]) {
                alreadyPlaced.Add(previousSurvivors[index]);
            }
        }

        DomElement? nextSibling = null;
        for (var index = nextOrder.Count - 1; index >= 0; index--) {
            var key = nextOrder[index];
            var element = _blockElements[key];
            if (!alreadyPlaced.Contains(key)) {
                sectionElement.InsertBefore(element, nextSibling);
            }
            nextSibling = element;
        }
    }

    private static (string Key, NotebookBlock Block)[] KeyBlocks(IReadOnlyList<NotebookBlock> blocks)
    {
        var result = new (string Key, NotebookBlock Block)[blocks.Count];
        for (var index = 0; index < blocks.Count; index++) {
            var block = blocks[index];
            var key = block switch {
                CodeCellBlock cell => cell.Id,
                ParagraphBlock paragraph => paragraph.Id,
                ListBlock list => list.Id,
                var unknown => throw new InvalidOperationException(
                    $"Unsupported block type '{unknown.GetType().Name}'."),
            };
            result[index] = (key, block);
        }
        return result;
    }

    private DomElement CreateBlockElement(NotebookBlock block, NotebookDockState dockState)
        => block switch {
            ParagraphBlock { Editable: true } paragraph => CreateEditableParagraphElement(paragraph),
            ParagraphBlock paragraph => CreateStaticParagraphElement(paragraph),
            ListBlock list => CreateListElement(list),
            CodeCellBlock cell => CreateCellElement(cell, dockState),
            var unknown => throw new InvalidOperationException(
                $"Unsupported block type '{unknown.GetType().Name}'."),
        };

    private void RemoveBlockElement(string key, NotebookBlock block, NotebookDockState dockState)
    {
        if (!_blockElements.Remove(key, out var element)) {
            return;
        }
        if (block is CodeCellBlock cell) {
            RemoveCellView(cell, dockState);
            return;
        }
        element.Remove();
        element.Dispose();
    }

    private DomElement CreateStaticParagraphElement(ParagraphBlock paragraph)
    {
        var element = DomElement.Create("p").Class("paragraph");
        AppendInlines(element, paragraph.Inlines);
        return element;
    }

    private DomElement CreateListElement(ListBlock list)
    {
        var element = DomElement.Create("ul").Class("list");
        foreach (var item in list.Items) {
            using var listItem = DomElement.Create("li");
            AppendInlines(listItem, item);
            element.Append(listItem);
        }
        return element;
    }

    private DomElement CreateCellElement(CodeCellBlock cell, NotebookDockState dockState)
    {
        var scriptWindow = dockState.GetWindow(cell.Id, DockWindowKind.Script);
        var outputWindow = dockState.GetWindow(cell.Id, DockWindowKind.Output);
        var renderWindow = dockState.GetWindow(cell.Id, DockWindowKind.Render);
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
        return region;
    }

    private void RemoveCellView(CodeCellBlock cell, NotebookDockState dockState)
    {
        if (!_cells.Remove(cell.Id, out var cellView)) {
            return;
        }
        UnregisterCellWindow(cellView.Script.Window, dockState);
        UnregisterCellWindow(cellView.Output.Window, dockState);
        UnregisterCellWindow(cellView.Render.Window, dockState);
        _dock.UnregisterRegion(cellView.Script.Window.HomeRegionId);
        _editors.Remove(cell.Id);
        cellView.Dispose();
    }

    private void UnregisterCellWindow(DockWindow window, NotebookDockState dockState)
        => _dock.UnregisterWindow(window.Id, dockState.GetTabForWindow(window.Id).Id);

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

    private DomElement CreateEditableParagraphElement(ParagraphBlock paragraph)
    {
        var elementId = NotebookElementIds.Paragraph(paragraph.Id);
        var text = FlattenInlines(paragraph.Inlines);
        var wrapper = DomElement.Create("div").Class("paragraph-editor");
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
        return wrapper;
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
