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
    private readonly BrowserNotebookFileTree _fileTree;
    private readonly BrowserCellWorkspaceView _cellWorkspace;
    private readonly Dictionary<string, BrowserCellView> _cellsByBlock = [];
    private readonly Dictionary<string, BrowserCellView> _cellsByScript = [];
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
        NotebookCellState cellState)
    {
        _container = DomElement.Find("notebook");
        _root = DomElement.Create("div").Class("notebook-document");
        _editors = new(world, references);
        _packages = new();
        _fileTree = new(document, cellState);
        _floatingLayer = DomElement.Create("div")
            .Class("section")
            .Class("floating-layer");
        _cellWorkspace = new(_floatingLayer);

        RenderTitleBar(document.Title);

        for (var index = 0; index < document.Sections.Count; index++) {
            var section = document.Sections[index];
            var sectionElement = CreateSectionShell(section, index);
            _sectionElements.Add(section.Id, sectionElement);
            _root.Append(sectionElement);
            ReconcileSectionBlocks(section.Id, [], section.Blocks, cellState, cellState);
        }
        _root.Append(_floatingLayer);

        _container.Text(string.Empty).Append(_root);
    }

    public BrowserEditorRegistry Editors => _editors;

    public void UpdateCellScope(string cellId, string? scope)
    {
        if (_cellsByBlock.TryGetValue(cellId, out var cell)) {
            cell.UpdateScope(scope);
        }
    }

    public void BeginEditorEdit(string cellId)
    {
        if (_cellsByScript.TryGetValue(cellId, out var cell)) {
            cell.BeginEditing(cellId);
        }
    }

    public void UpdateEditorDirty(string cellId)
    {
        if (_cellsByScript.TryGetValue(cellId, out var cell)) {
            cell.UpdateDirtyState(cellId);
        }
    }

    public void EndEditorEdit(string cellId)
    {
        if (_cellsByScript.TryGetValue(cellId, out var cell)) {
            cell.EndEditing(cellId);
        }
    }

    public void DiscardEditor(string cellId)
    {
        if (_cellsByScript.TryGetValue(cellId, out var cell)) {
            cell.DiscardChanges(cellId);
        }
    }

    void IRenderHost<NotebookCellSnapshot>.Upsert(in NotebookCellSnapshot view)
        => _cellsByScript[view.Id].Update(view.Id, view.State);

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

    void IRenderHost<NotebookCellPresentation>.Upsert(
        in NotebookCellPresentation view)
    {
        _cellWorkspace.Apply(view.State);
        _fileTree.UpdateSelection(view.State);
    }

    void IRenderHost<NotebookCellPresentation>.Remove(
        in NotebookCellPresentation view)
    {
    }

    public void ReconcileDocument(
        NotebookDocument previousDocument,
        NotebookDocument nextDocument,
        NotebookCellState previousCellState,
        NotebookCellState nextCellState)
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
            ReconcileSectionBlocks(sectionId, section.Blocks, [], previousCellState, nextCellState);
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
            ReconcileSectionBlocks(section.Id, previousBlocks, section.Blocks, previousCellState, nextCellState);
        }

        if (sectionsChanged) {
            for (var sectionIndex = 0; sectionIndex < nextDocument.Sections.Count; sectionIndex++) {
                var sectionId = nextDocument.Sections[sectionIndex].Id;
                _sectionTitleInputs[sectionId].Attr("aria-label", $"Section {sectionIndex + 1} title");
            }
        }
        _fileTree.Update(nextDocument, nextCellState);
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _cellWorkspace.Dispose();
        _editors.Dispose();
        foreach (var cell in _cellsByBlock.Values) {
            cell.Dispose();
        }
        _cellsByBlock.Clear();
        _cellsByScript.Clear();
        _fileTree.Dispose();
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
        NotebookCellState previousCellState,
        NotebookCellState nextCellState)
    {
        var previousKeyed = KeyBlocks(previousBlocks);
        var nextKeyed = KeyBlocks(nextBlocks);
        var previousOrder = previousKeyed.Select(static entry => entry.Key).ToList();
        var nextOrder = nextKeyed.Select(static entry => entry.Key).ToList();

        ReconcileSurvivingCells(previousKeyed, nextKeyed, previousCellState, nextCellState);

        if (previousOrder.SequenceEqual(nextOrder, StringComparer.Ordinal)) {
            return;
        }

        var nextKeys = new HashSet<string>(nextOrder, StringComparer.Ordinal);
        foreach (var (key, block) in previousKeyed) {
            if (!nextKeys.Contains(key)) {
                RemoveBlockElement(key, block, previousCellState);
            }
        }

        var previousKeys = new HashSet<string>(previousOrder, StringComparer.Ordinal);
        foreach (var (key, block) in nextKeyed) {
            if (!previousKeys.Contains(key)) {
                _blockElements.Add(key, CreateBlockElement(block, nextCellState));
            }
        }

        if (_sectionElements.TryGetValue(sectionId, out var sectionElement)) {
            var trackedOrder = _sectionBlockOrder.TryGetValue(sectionId, out var tracked) ? tracked : [];
            RepositionBlocks(sectionElement, trackedOrder, nextOrder);
        }
        _sectionBlockOrder[sectionId] = nextOrder;

        RefreshSectionActions(sectionId, nextBlocks);
    }

    private void ReconcileSurvivingCells(
        (string Key, NotebookBlock Block)[] previousKeyed,
        (string Key, NotebookBlock Block)[] nextKeyed,
        NotebookCellState previousCellState,
        NotebookCellState nextCellState)
    {
        var previousByKey = previousKeyed.ToDictionary(static entry => entry.Key, static entry => entry.Block);
        foreach (var (key, block) in nextKeyed) {
            if (block is not CodeCellBlock nextCell
                || !previousByKey.TryGetValue(key, out var previousBlock)
                || previousBlock is not CodeCellBlock previousCell
                || !_cellsByBlock.TryGetValue(key, out var cellView)) {
                continue;
            }
            ReconcileCellScripts(cellView, previousCell, nextCell, previousCellState, nextCellState);
        }
    }

    private void ReconcileCellScripts(
        BrowserCellView cellView,
        CodeCellBlock previousCell,
        CodeCellBlock nextCell,
        NotebookCellState previousCellState,
        NotebookCellState nextCellState)
    {
        cellView.UpdateScope(nextCell.Scope);
        var previousIds = new HashSet<string>(
            previousCell.Scripts.Select(static script => script.Id), StringComparer.Ordinal);
        var nextIds = new HashSet<string>(
            nextCell.Scripts.Select(static script => script.Id), StringComparer.Ordinal);
        if (previousIds.SetEquals(nextIds)) {
            return;
        }

        foreach (var script in previousCell.Scripts) {
            if (nextIds.Contains(script.Id)) {
                continue;
            }
            var window = cellView.RemoveScript(script.Id);
            _cellsByScript.Remove(script.Id);
            _editors.Remove(script.Id);
            if (window is not null) {
                UnregisterCellWindow(window.Window, previousCellState);
            }
        }

        foreach (var script in nextCell.Scripts) {
            if (previousIds.Contains(script.Id)) {
                continue;
            }
            var window = cellView.AddScript(script, nextCellState.GetScriptWindow(script.Id));
            _cellsByScript.Add(script.Id, cellView);
            _cellWorkspace.RegisterWindow(window);
        }
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

    private DomElement CreateBlockElement(NotebookBlock block, NotebookCellState cellState)
        => block switch {
            ParagraphBlock { Editable: true } paragraph => CreateEditableParagraphElement(paragraph),
            ParagraphBlock paragraph => CreateStaticParagraphElement(paragraph),
            ListBlock list => CreateListElement(list),
            CodeCellBlock cell => CreateCellElement(cell, cellState),
            var unknown => throw new InvalidOperationException(
                $"Unsupported block type '{unknown.GetType().Name}'."),
        };

    private void RemoveBlockElement(string key, NotebookBlock block, NotebookCellState cellState)
    {
        if (!_blockElements.Remove(key, out var element)) {
            return;
        }
        if (block is CodeCellBlock cell) {
            RemoveCellView(cell, cellState);
            return;
        }
        element.Remove();
        element.Dispose();
    }

    private DomElement CreateCellElement(CodeCellBlock cell, NotebookCellState cellState)
    {
        var outputWindow = cellState.GetWindow(cell.Id, CellWindowKind.Output);
        var renderWindow = cellState.GetWindow(cell.Id, CellWindowKind.Render);
        var scriptWindows = cell.Scripts
            .Select(script => (script, Window: cellState.GetScriptWindow(script.Id)))
            .ToArray();

        var cellView = new BrowserCellView(cell, scriptWindows, _editors, outputWindow, renderWindow);
        _cellsByBlock.Add(cell.Id, cellView);
        foreach (var script in cell.Scripts) {
            _cellsByScript.Add(script.Id, cellView);
        }

        _cellWorkspace.RegisterWindow(cellView.Output);
        _cellWorkspace.RegisterWindow(cellView.Render);
        foreach (var scriptWindow in cellView.Scripts) {
            _cellWorkspace.RegisterWindow(scriptWindow);
        }

        var region = DomElement.Create("div");
        _cellWorkspace.RegisterRegion(
            scriptWindows[0].Window.HomeRegionId,
            cell.Id,
            region);
        return region;
    }

    private void RemoveCellView(CodeCellBlock cell, NotebookCellState cellState)
    {
        if (!_cellsByBlock.Remove(cell.Id, out var cellView)) {
            return;
        }
        foreach (var script in cell.Scripts) {
            _cellsByScript.Remove(script.Id);
            _editors.Remove(script.Id);
        }
        foreach (var scriptWindow in cellView.Scripts) {
            UnregisterCellWindow(scriptWindow.Window, cellState);
        }
        UnregisterCellWindow(cellView.Output.Window, cellState);
        UnregisterCellWindow(cellView.Render.Window, cellState);
        _cellWorkspace.UnregisterRegion(cellView.Scripts[0].Window.HomeRegionId);
        cellView.Dispose();
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

    private void UnregisterCellWindow(CellWindow window, NotebookCellState cellState)
        => _cellWorkspace.UnregisterWindow(window.Id, cellState.GetTabForWindow(window.Id).Id);

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
        using var host = DomElement.Create("details").Class("menu-toggle");
        using var summary = DomElement.Create("summary")
            .Class("icon-btn")
            .Attr("aria-label", label)
            .Attr("title", label)
            .Text("+");
        using var menu = DomElement.Create("div").Class("menu-popover");
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
            .Class("menu-item")
            .Attr("type", "button")
            .On("click", payload);
        using var glyph = DomElement.Create("span")
            .Class("menu-icon")
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
