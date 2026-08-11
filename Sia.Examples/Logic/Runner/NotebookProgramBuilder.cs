using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sia_Examples.Notebook;

public static class NotebookProgramBuilder
{
    public const string WrapperNamespace = "NotebookCell";

    private const char _marker = '\uE000';
    private const string _renderStartToken = "\uE001R\uE001";
    private const string _renderEndToken = "\uE001E\uE001";

    private static string StartToken(int index) => $"{_marker}S{index}{_marker}";
    private static string EndToken(int index) => $"{_marker}E{index}{_marker}";

    public static NotebookProgram Build(IReadOnlyList<(string Id, string Source)> cells)
    {
        var builder = new StringBuilder();
        var ranges = new List<CellRange>(cells.Count);
        var typeParts = new List<(string Id, string Text)>(cells.Count);
        var line = 0;

        void Emit(string text)
        {
            builder.Append(text);
            foreach (var character in text) {
                if (character == '\n') {
                    line++;
                }
            }
        }

        var splits = cells.Select(c => (c.Id, Parts: Split(c.Source))).ToList();
        foreach (var (_, parts) in splits) {
            if (parts.Usings.Length > 0) {
                Emit(EnsureTrailingNewline(parts.Usings));
            }
        }

        Emit("var world = new global::Sia.World();\n");
        Emit("global::Sia.Context<global::Sia.World>.Current = world;\n");
        Emit("try {\n");

        for (var index = 0; index < splits.Count; index++) {
            var (id, parts) = splits[index];
            var (_, statements, types) = parts;
            var startToken = StartToken(index);
            var endToken = EndToken(index);

            Emit("{\n");
            Emit($"global::System.Console.Out.Write(\"{startToken}\");\n");
            Emit($"global::System.Console.Error.Write(\"{startToken}\");\n");
            Emit("try {\n");
            var statementsStartLine = line;
            Emit(EnsureTrailingNewline(statements));
            Emit("}\n");
            Emit("catch (global::System.Exception __sia_cell_ex) {\n");
            Emit("global::System.Console.Error.WriteLine(__sia_cell_ex.ToString());\n");
            Emit("}\n");
            Emit("finally {\n");
            Emit($"global::System.Console.Out.Write(\"{endToken}\");\n");
            Emit($"global::System.Console.Error.Write(\"{endToken}\");\n");
            Emit("}\n");
            Emit("}\n");

            typeParts.Add((id, types));
            ranges.Add(new(id, statementsStartLine, null, startToken, endToken));
        }

        Emit("}\n");
        Emit("finally {\n");
        Emit("global::Sia.Context<global::Sia.World>.Current = null;\n");
        Emit("world.Dispose();\n");
        Emit("}\n");

        Emit($"\nnamespace {WrapperNamespace} {{\n");
        Emit("public static class Notebook {\n");
        Emit("public static void Render(object? value = null) {\n");
        Emit($"global::System.Console.Out.Write(\"{_renderStartToken}\");\n");
        Emit("if (value is not null) global::System.Console.Out.Write(value);\n");
        Emit($"global::System.Console.Out.Write(\"{_renderEndToken}\");\n");
        Emit("}\n}\n");
        for (var index = 0; index < typeParts.Count; index++) {
            var (id, text) = typeParts[index];
            if (text.Length == 0) {
                continue;
            }
            var typesStartLine = line;
            Emit(EnsureTrailingNewline(text));
            ranges[index] = ranges[index] with { TypesStartLine = typesStartLine };
        }
        Emit("}\n");

        return new NotebookProgram(builder.ToString(), true, ranges);
    }

    public static IReadOnlyDictionary<string, string> SliceOutput(string captured, NotebookProgram program)
    {
        Dictionary<string, string> result = [];
        foreach (var range in program.CellRanges) {
            var startIndex = captured.IndexOf(range.StartToken, StringComparison.Ordinal);
            if (startIndex < 0) {
                continue;
            }
            startIndex += range.StartToken.Length;
            var endIndex = captured.IndexOf(range.EndToken, startIndex, StringComparison.Ordinal);
            result[range.CellId] = endIndex >= 0
                ? captured[startIndex..endIndex]
                : captured[startIndex..];
        }
        return result;
    }

    public static (string StandardOutput, string RenderOutput, bool RenderRequested)
        SplitRenderOutput(string output)
    {
        var standard = new StringBuilder(output.Length);
        var render = new StringBuilder();
        var position = 0;
        var requested = false;
        while (position < output.Length) {
            var start = output.IndexOf(_renderStartToken, position, StringComparison.Ordinal);
            if (start < 0) {
                standard.Append(output, position, output.Length - position);
                break;
            }
            standard.Append(output, position, start - position);
            var contentStart = start + _renderStartToken.Length;
            var end = output.IndexOf(_renderEndToken, contentStart, StringComparison.Ordinal);
            if (end < 0) {
                standard.Append(output, start, output.Length - start);
                break;
            }
            if (render.Length > 0) {
                render.AppendLine();
            }
            render.Append(output, contentStart, end - contentStart);
            requested = true;
            position = end + _renderEndToken.Length;
        }
        return (standard.ToString(), render.ToString(), requested);
    }

    private static string EnsureTrailingNewline(string text)
        => text.Length == 0 || text[^1] == '\n' ? text : text + "\n";

    private static (string Usings, string Statements, string Types) Split(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var usingsEnd = root.Usings.Count > 0 ? root.Usings[^1].FullSpan.End : 0;

        var firstTypeMember = root.Members.FirstOrDefault(m => m is not GlobalStatementSyntax);
        if (firstTypeMember is null) {
            return (source[..usingsEnd], source[usingsEnd..], "");
        }

        var insertAt = firstTypeMember.SpanStart;
        return (source[..usingsEnd], source[usingsEnd..insertAt], source[insertAt..]);
    }
}
