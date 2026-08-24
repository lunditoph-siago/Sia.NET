using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sia_Examples.Notebook;

public static class NotebookDocumentParser
{
    private static readonly Regex _whitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public static NotebookDocument Parse(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new FormatException("Notebook document has no root element.");
        if (root.Name.LocalName != "Notebook") {
            throw new FormatException(
                $"Expected root element <Notebook>, found <{root.Name.LocalName}>.");
        }

        var title = (string?)root.Attribute("Title") ?? "";
        var packages = root.Element("Packages")?
            .Elements("Package")
            .Select(ParsePackageRef)
            .ToList()
            ?? [];
        var sections = root.Elements("Section").Select(ParseSection).ToList();
        return new(title, packages, sections);
    }

    private static PackageRef ParsePackageRef(XElement element)
    {
        var id = (string?)element.Attribute("Id")
            ?? throw new FormatException("<Package> requires an Id attribute.");
        var version = (string?)element.Attribute("Version");
        var analyzer = (bool?)element.Attribute("Analyzer");
        return new(id, version, analyzer ?? false);
    }

    private static NotebookSection ParseSection(XElement element)
    {
        var id = (string?)element.Attribute("Id") ?? Guid.NewGuid().ToString("N");
        var title = (string?)element.Attribute("Title") ?? "";
        List<NotebookBlock> blocks = [];
        foreach (var child in element.Elements()) {
            blocks.Add(child.Name.LocalName switch {
                "Paragraph" => new ParagraphBlock(
                    (string?)child.Attribute("Id") ?? Guid.NewGuid().ToString("N"),
                    ParseInlines(child),
                    (bool?)child.Attribute("Editable") ?? false),
                "List" => new ListBlock(
                    (string?)child.Attribute("Id") ?? Guid.NewGuid().ToString("N"),
                    child.Elements("Item").Select(ParseInlines).ToList()),
                "CodeCell" => ParseCodeCell(child),
                var unknown => throw new FormatException(
                    $"Unknown block element <{unknown}> in <Section Title=\"{title}\">."),
            });
        }
        return new(id, title, blocks);
    }

    private static CodeCellBlock ParseCodeCell(XElement element)
    {
        var cellId = (string?)element.Attribute("Id") ?? Guid.NewGuid().ToString("N");
        var scripts = element.Elements("Script").Select(ParseScript).ToList();
        if (scripts.Count == 0) {
            throw new FormatException($"<CodeCell Id=\"{cellId}\"> must contain at least one <Script> element.");
        }
        return new(
            cellId,
            scripts,
            (bool?)element.Attribute("Editable") ?? true,
            (string?)element.Attribute("Scope"));
    }

    private static CellScript ParseScript(XElement element)
        => new(
            (string?)element.Attribute("Id") ?? Guid.NewGuid().ToString("N"),
            DedentCode(element.Value),
            (string?)element.Attribute("Name"));

    private static IReadOnlyList<Inline> ParseInlines(XElement element)
    {
        List<Inline> inlines = [];
        foreach (var node in element.Nodes()) {
            switch (node) {
                case XText text:
                    AddTextInline(inlines, text.Value);
                    break;
                case XElement { Name.LocalName: "Code" } code:
                    inlines.Add(new CodeInline(NormalizeWhitespace(code.Value)));
                    break;
            }
        }
        return inlines;
    }

    private static void AddTextInline(List<Inline> inlines, string text)
    {
        var normalized = NormalizeWhitespace(text);
        if (normalized.Length > 0) {
            inlines.Add(new TextInline(normalized));
        }
    }

    private static string NormalizeWhitespace(string text)
        => _whitespaceRun.Replace(text, " ");

    private static string DedentCode(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var start = FirstContentLine(lines);
        var end = LastContentLine(lines, start);
        if (start > end) {
            return "";
        }

        var indent = FindIndent(lines, start, end);
        var result = new string[end - start + 1];
        for (var index = start; index <= end; index++) {
            result[index - start] = lines[index].Length >= indent
                ? lines[index][indent..]
                : lines[index];
        }
        return string.Join('\n', result);
    }

    private static int FirstContentLine(string[] lines)
    {
        var index = 0;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) {
            index++;
        }
        return index;
    }

    private static int LastContentLine(string[] lines, int start)
    {
        var index = lines.Length - 1;
        while (index >= start && string.IsNullOrWhiteSpace(lines[index])) {
            index--;
        }
        return index;
    }

    private static int FindIndent(string[] lines, int start, int end)
    {
        var indent = int.MaxValue;
        for (var index = start; index <= end; index++) {
            if (string.IsNullOrWhiteSpace(lines[index])) {
                continue;
            }
            var leading = lines[index].Length - lines[index].TrimStart(' ').Length;
            indent = Math.Min(indent, leading);
        }
        return indent == int.MaxValue ? 0 : indent;
    }
}
