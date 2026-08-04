using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sia_Examples.Notebook;

public abstract record Inline;
public sealed record TextInline(string Text) : Inline;
public sealed record CodeInline(string Text) : Inline;

public abstract record NotebookBlock;
public sealed record ParagraphBlock(IReadOnlyList<Inline> Inlines) : NotebookBlock;
public sealed record ListBlock(IReadOnlyList<IReadOnlyList<Inline>> Items) : NotebookBlock;
public sealed record CodeCellBlock(string Id, string InitialSource, bool Editable, string? Scope) : NotebookBlock;

public sealed record NotebookSection(string Title, IReadOnlyList<NotebookBlock> Blocks);
public sealed record NotebookDocument(string Title, IReadOnlyList<NotebookSection> Sections);

public static class NotebookDocumentParser
{
    public static NotebookDocument Parse(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new FormatException("Notebook document has no root element.");
        if (root.Name.LocalName != "Notebook") {
            throw new FormatException($"Expected root element <Notebook>, found <{root.Name.LocalName}>.");
        }

        var title = (string?)root.Attribute("Title") ?? "";
        var sections = root.Elements("Section").Select(ParseSection).ToList();
        return new(title, sections);
    }

    private static NotebookSection ParseSection(XElement element)
    {
        var title = (string?)element.Attribute("Title") ?? "";
        var blocks = new List<NotebookBlock>();
        foreach (var child in element.Elements()) {
            blocks.Add(child.Name.LocalName switch {
                "Paragraph" => new ParagraphBlock(ParseInlines(child)),
                "List" => new ListBlock(child.Elements("Item").Select(ParseInlines).ToList()),
                "CodeCell" => new CodeCellBlock(
                    (string?)child.Attribute("Id") ?? Guid.NewGuid().ToString("N"),
                    DedentCode(child.Value),
                    (bool?)child.Attribute("Editable") ?? true,
                    (string?)child.Attribute("Scope")),
                var unknown => throw new FormatException($"Unknown block element <{unknown}> in <Section Title=\"{title}\">."),
            });
        }
        return new(title, blocks);
    }

    private static IReadOnlyList<Inline> ParseInlines(XElement element)
    {
        var inlines = new List<Inline>();
        foreach (var node in element.Nodes()) {
            switch (node) {
                case XText text: {
                    var normalized = NormalizeWhitespace(text.Value);
                    if (normalized.Length > 0) {
                        inlines.Add(new TextInline(normalized));
                    }
                    break;
                }
                case XElement { Name.LocalName: "Code" } code:
                    inlines.Add(new CodeInline(NormalizeWhitespace(code.Value)));
                    break;
            }
        }
        return inlines;
    }

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private static string NormalizeWhitespace(string text) => WhitespaceRun.Replace(text, " ");

    private static string DedentCode(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');

        var start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0) {
            start++;
        }
        var end = lines.Length - 1;
        while (end >= start && lines[end].Trim().Length == 0) {
            end--;
        }
        if (start > end) {
            return "";
        }

        var indent = int.MaxValue;
        for (var i = start; i <= end; i++) {
            if (lines[i].Trim().Length == 0) {
                continue;
            }
            var leading = lines[i].Length - lines[i].TrimStart(' ').Length;
            indent = Math.Min(indent, leading);
        }
        if (indent == int.MaxValue) {
            indent = 0;
        }

        var result = new List<string>(end - start + 1);
        for (var i = start; i <= end; i++) {
            result.Add(lines[i].Length >= indent ? lines[i][indent..] : lines[i]);
        }
        return string.Join('\n', result);
    }
}
