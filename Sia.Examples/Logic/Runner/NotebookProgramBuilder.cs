using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sia_Examples.Notebook;

public static class NotebookProgramBuilder
{
    public const string WrapperNamespace = "NotebookCell";

    private const char _marker = '\uE000';

    private static string StartToken(int index) => $"{_marker}S{index}{_marker}";
    private static string EndToken(int index) => $"{_marker}E{index}{_marker}";

    public static NotebookProgram Build(IReadOnlyList<NotebookProgramCell> cells)
    {
        var builder = new StringBuilder();
        var ranges = new List<CellRange>(cells.Count);
        var sources = new List<CSharpSourceDocument>();
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

        var fileIndex = 0;
        var splitCells = cells.Select(cell => (
            cell.Id,
            Files: cell.Files.Select(file => {
                var path = $"Notebook/File{fileIndex++}.cs";
                return (File: file, Path: path, Parts: Split(file.Source));
            }).ToArray())).ToArray();
        var emittedUsings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, files) in splitCells) {
            foreach (var (_, _, parts) in files) {
                foreach (var usingDirective in parts.RunnerUsings) {
                    if (emittedUsings.Add(usingDirective)) {
                        Emit(EnsureTrailingNewline(usingDirective));
                    }
                }
            }
        }

        Emit("var world = new global::Sia.World();\n");
        Emit("try {\n");

        for (var index = 0; index < splitCells.Length; index++) {
            var (id, files) = splitCells[index];
            var startToken = StartToken(index);
            var endToken = EndToken(index);

            Emit($"global::System.Console.Out.Write(\"{startToken}\");\n");
            Emit($"global::System.Console.Error.Write(\"{startToken}\");\n");
            var statementsStartLine = line;
            foreach (var (_, path, parts) in files) {
                if (parts.Statements.Length == 0) {
                    continue;
                }
                Emit($"#line {parts.StatementsStartLine} \"{EscapeDirectivePath(path)}\"\n");
                Emit(EnsureTrailingNewline(parts.Statements));
                Emit("#line default\n");
                Emit("#line hidden\n");
            }
            Emit($"global::System.Console.Out.Write(\"{endToken}\");\n");
            Emit($"global::System.Console.Error.Write(\"{endToken}\");\n");

            ranges.Add(new(id, statementsStartLine, null, startToken, endToken));
        }

        Emit("}\n");
        Emit("finally {\n");
        Emit("world.Dispose();\n");
        Emit("}\n");

        sources.Add(new(
            "$runner",
            "Notebook/Runner.g.cs",
            "Runner.g.cs",
            builder.ToString(),
            IsUserCode: false));
        foreach (var (_, files) in splitCells) {
            foreach (var (file, path, parts) in files) {
                sources.Add(new(
                    file.Id,
                    path,
                    file.Name,
                    BuildFileSource(path, parts)));
            }
        }

        return new NotebookProgram(builder.ToString(), true, ranges, sources);
    }

    private static string BuildFileSource(string path, SourceParts parts)
    {
        var builder = new StringBuilder();
        builder.Append(EnsureTrailingNewline(parts.Usings));
        if (parts.Types.Length > 0) {
            builder.Append("#line ").Append(parts.TypesStartLine)
                .Append(" \"").Append(EscapeDirectivePath(path)).Append("\"\n");
            builder.Append(EnsureTrailingNewline(parts.Types));
            builder.Append("#line default\n");
        }
        return builder.ToString();
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
        => RenderOutputProtocol.Split(output);

    internal static string BuildStandaloneRenderSupport()
        => RenderOutputProtocol.BuildSupportSource();

    private static string EnsureTrailingNewline(string text)
        => text.Length == 0 || text[^1] == '\n' ? text : text + "\n";

    private static string EscapeDirectivePath(string path)
        => path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static SourceParts Split(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var text = tree.GetText();

        var usingsEnd = root.Usings.Count > 0 ? root.Usings[^1].FullSpan.End : 0;
        var runnerUsings = root.Usings
            .Select(static usingDirective => usingDirective.WithoutTrivia().ToFullString())
            .ToArray();
        var statementsStartLine = text.Lines.GetLineFromPosition(usingsEnd).LineNumber + 1;

        var firstTypeMember = root.Members.FirstOrDefault(m => m is not GlobalStatementSyntax);
        if (firstTypeMember is null) {
            return new(
                source[..usingsEnd],
                source[usingsEnd..],
                "",
                statementsStartLine,
                statementsStartLine,
                runnerUsings);
        }

        var insertAt = firstTypeMember.SpanStart;
        var typesStartLine = text.Lines.GetLineFromPosition(insertAt).LineNumber + 1;
        return new(
            source[..usingsEnd],
            source[usingsEnd..insertAt],
            source[insertAt..],
            statementsStartLine,
            typesStartLine,
            runnerUsings);
    }

    private readonly record struct SourceParts(
        string Usings,
        string Statements,
        string Types,
        int StatementsStartLine,
        int TypesStartLine,
        IReadOnlyList<string> RunnerUsings);
}
