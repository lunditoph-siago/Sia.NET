using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sia_Examples.Notebook;

public readonly record struct CellRange(
    string CellId,
    int StatementsStartLine,
    int? TypesStartLine,
    string StartToken,
    string EndToken);

public sealed record NotebookProgram(
    string Source,
    bool NeedsWrapperUsing,
    IReadOnlyList<CellRange> CellRanges)
{
    public string? ResolveCellId(int line)
    {
        string? owner = null;
        var bestStart = -1;
        foreach (var range in CellRanges) {
            if (range.StatementsStartLine <= line && range.StatementsStartLine > bestStart) {
                owner = range.CellId;
                bestStart = range.StatementsStartLine;
            }
            if (range.TypesStartLine is { } typesStart && typesStart <= line && typesStart > bestStart) {
                owner = range.CellId;
                bestStart = typesStart;
            }
        }
        return owner;
    }
}

public static class NotebookProgramBuilder
{
    public const string WrapperNamespace = "NotebookCell";

    private const char Marker = '\uE000';

    private static string StartToken(int index) => $"{Marker}S{index}{Marker}";
    private static string EndToken(int index) => $"{Marker}E{index}{Marker}";

    public static NotebookProgram Build(IReadOnlyList<(string Id, string Source)> cells)
    {
        var sb = new StringBuilder();
        var ranges = new List<CellRange>(cells.Count);
        var typeParts = new List<(string Id, string Text)>(cells.Count);
        var line = 0;

        void Emit(string text)
        {
            sb.Append(text);
            foreach (var c in text) {
                if (c == '\n') {
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

        for (var i = 0; i < splits.Count; i++) {
            var (id, parts) = splits[i];
            var (_, statements, types) = parts;
            var startTok = StartToken(i);
            var endTok = EndToken(i);

            Emit("{\n");
            Emit($"global::System.Console.Out.Write(\"{startTok}\");\n");
            Emit($"global::System.Console.Error.Write(\"{startTok}\");\n");
            Emit("try {\n");
            var statementsStartLine = line;
            Emit(EnsureTrailingNewline(statements));
            Emit("}\n");
            Emit("catch (global::System.Exception __sia_cell_ex) {\n");
            Emit("global::System.Console.Error.WriteLine(__sia_cell_ex.ToString());\n");
            Emit("}\n");
            Emit("finally {\n");
            Emit($"global::System.Console.Out.Write(\"{endTok}\");\n");
            Emit($"global::System.Console.Error.Write(\"{endTok}\");\n");
            Emit("}\n");
            Emit("}\n");

            typeParts.Add((id, types));
            ranges.Add(new CellRange(id, statementsStartLine, null, startTok, endTok));
        }

        Emit("}\n");
        Emit("finally {\n");
        Emit("global::Sia.Context<global::Sia.World>.Current = null;\n");
        Emit("world.Dispose();\n");
        Emit("}\n");

        var hasTypes = typeParts.Exists(t => t.Text.Length > 0);
        if (hasTypes) {
            Emit($"\nnamespace {WrapperNamespace} {{\n");
            for (var i = 0; i < typeParts.Count; i++) {
                var (id, text) = typeParts[i];
                if (text.Length == 0) {
                    continue;
                }
                var typesStartLine = line;
                Emit(EnsureTrailingNewline(text));
                ranges[i] = ranges[i] with { TypesStartLine = typesStartLine };
            }
            Emit("}\n");
        }

        return new NotebookProgram(sb.ToString(), hasTypes, ranges);
    }

    public static IReadOnlyDictionary<string, string> SliceOutput(string captured, NotebookProgram program)
    {
        var result = new Dictionary<string, string>();
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
