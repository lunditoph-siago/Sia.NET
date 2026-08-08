using Sia;
using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public sealed class BrowserNotebookView :
    INotebookView,
    IDisposable
{
    private readonly BrowserElement _container;
    private readonly BrowserElement _root;
    private readonly BrowserEditorRegistry _editors;
    private readonly BrowserPackagePanel _packages;
    private readonly Dictionary<string, BrowserCellView> _cells = [];
    private bool _disposed;

    public BrowserNotebookView(
        World world,
        NotebookDocument document,
        NotebookSessionSnapshot snapshot,
        ICompilationReferenceResolver references)
    {
        _container = BrowserElement.Find("notebook");
        _root = BrowserElement.Create("div");
        _editors = new(world, references);
        _packages = new();

        using var title = BrowserElement.Create("h2").Class("title").Text(document.Title);
        _root.Append(title);

        var cellNumbers = snapshot.Cells
            .Select(static (cell, index) => (cell.Id, Number: index + 1))
            .ToDictionary(static item => item.Id, static item => item.Number);
        foreach (var section in document.Sections) {
            RenderSection(section, cellNumbers);
        }

        _container.Text(string.Empty).Append(_root);
    }

    public BrowserEditorRegistry Editors => _editors;

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

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
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

    private void RenderSection(
        NotebookSection section,
        IReadOnlyDictionary<string, int> cellNumbers)
    {
        using var sectionElement = BrowserElement.Create("section").Class("section");
        using var heading = BrowserElement.Create("h3")
            .Class("section-title")
            .Text(section.Title);
        sectionElement.Append(heading);

        foreach (var block in section.Blocks) {
            switch (block) {
                case ParagraphBlock paragraph:
                    using (var element = BrowserElement.Create("p").Class("paragraph")) {
                        AppendInlines(element, paragraph.Inlines);
                        sectionElement.Append(element);
                    }
                    break;
                case ListBlock list:
                    using (var element = BrowserElement.Create("ul").Class("list")) {
                        foreach (var item in list.Items) {
                            using var listItem = BrowserElement.Create("li");
                            AppendInlines(listItem, item);
                            element.Append(listItem);
                        }
                        sectionElement.Append(element);
                    }
                    break;
                case CodeCellBlock cell:
                    var cellView = new BrowserCellView(
                        cellNumbers[cell.Id],
                        cell,
                        _editors);
                    _cells.Add(cell.Id, cellView);
                    sectionElement.Append(cellView.Root);
                    break;
            }
        }
        _root.Append(sectionElement);
    }

    private static void AppendInlines(
        BrowserElement parent,
        IReadOnlyList<Inline> inlines)
    {
        foreach (var inline in inlines) {
            using var child = inline switch {
                TextInline text => BrowserElement.CreateText(text.Text),
                CodeInline code => BrowserElement.Create("code").Text(code.Text),
                _ => throw new InvalidOperationException(
                    $"Unsupported inline type '{inline.GetType().Name}'."),
            };
            parent.Append(child);
        }
    }
}
