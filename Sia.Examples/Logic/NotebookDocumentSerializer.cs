using System.Xml.Linq;

namespace Sia_Examples.Notebook;

public static class NotebookDocumentSerializer
{
    private const int CodeIndent = 4;

    public static string Write(NotebookDocument document)
    {
        var root = new XElement("Notebook");
        if (document.Title.Length > 0) {
            root.SetAttributeValue("Title", document.Title);
        }

        List<XElement> topLevel = [];
        if (document.Packages.Count > 0) {
            var packages = new XElement("Packages");
            packages.Add(Laid(document.Packages.Select(WritePackageRef), indent: 4));
            topLevel.Add(packages);
        }
        topLevel.AddRange(document.Sections.Select(WriteSection));
        root.Add(Laid(topLevel, indent: 2));

        return new XDocument(root).ToString(SaveOptions.DisableFormatting);
    }

    private static IEnumerable<object> Laid(IEnumerable<XElement> children, int indent)
    {
        var pad = new string(' ', indent);
        var any = false;
        foreach (var child in children) {
            any = true;
            yield return new XText("\n" + pad);
            yield return child;
        }
        if (any) {
            yield return new XText("\n" + new string(' ', indent - 2));
        }
    }

    private static XElement WritePackageRef(PackageRef package)
    {
        var element = new XElement("Package",
            new XAttribute("Id", package.Id));
        if (package.Version is { Length: > 0 } version) {
            element.SetAttributeValue("Version", version);
        }
        if (package.Analyzer) {
            element.SetAttributeValue("Analyzer", "true");
        }
        return element;
    }

    private static XElement WriteSection(NotebookSection section)
    {
        var element = new XElement("Section", new XAttribute("Id", section.Id));
        if (section.Title.Length > 0) {
            element.SetAttributeValue("Title", section.Title);
        }
        element.Add(Laid(section.Blocks.Select(WriteBlock), indent: 4));
        return element;
    }

    private static XElement WriteBlock(NotebookBlock block)
        => block switch {
            ParagraphBlock paragraph => WriteParagraph(paragraph),
            ListBlock list => new XElement("List",
                Laid(list.Items.Select(item => new XElement("Item", WriteInlines(item))), indent: 6)),
            CodeCellBlock cell => WriteCodeCell(cell),
            var unknown => throw new NotSupportedException(
                $"Don't know how to serialize block type '{unknown.GetType().Name}'."),
        };

    private static XElement WriteParagraph(ParagraphBlock paragraph)
    {
        var element = new XElement("Paragraph");
        if (paragraph.Editable) {
            element.SetAttributeValue("Id", paragraph.Id);
            element.SetAttributeValue("Editable", "true");
        }
        element.Add(WriteInlines(paragraph.Inlines));
        return element;
    }

    private static IEnumerable<XNode> WriteInlines(IReadOnlyList<Inline> inlines)
        => inlines.Select(static inline => inline switch {
            TextInline text => (XNode)new XText(text.Text),
            CodeInline code => new XElement("Code", code.Text),
            var unknown => throw new NotSupportedException(
                $"Don't know how to serialize inline type '{unknown.GetType().Name}'."),
        });

    private static XElement WriteCodeCell(CodeCellBlock cell)
    {
        var element = new XElement("CodeCell",
            new XAttribute("Id", cell.Id),
            new XAttribute("Editable", cell.Editable ? "true" : "false"));
        if (cell.Scope is { Length: > 0 } scope) {
            element.SetAttributeValue("Scope", scope);
        }
        element.Add(Laid(cell.Scripts.Select(WriteScript), indent: 6));
        return element;
    }

    private static XElement WriteScript(CellScript script)
    {
        var element = new XElement("Script", new XAttribute("Id", script.Id));
        if (script.Name is { Length: > 0 } name) {
            element.SetAttributeValue("Name", name);
        }
        element.Add(new XCData('\n' + IndentCode(script.InitialSource) + '\n'));
        return element;
    }

    private static string IndentCode(string source)
    {
        var indent = new string(' ', CodeIndent);
        var lines = source.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Select(line => line.Length == 0 ? line : indent + line));
    }
}
