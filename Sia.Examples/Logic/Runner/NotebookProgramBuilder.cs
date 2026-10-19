using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sia.Examples.Notebook;

public static class NotebookProgramBuilder
{
    public const string WrapperNamespace = "NotebookCell";

    private const char k_Marker = '\uE000';

    private static string StartToken(int index) => $"{k_Marker}S{index}{k_Marker}";
    private static string EndToken(int index) => $"{k_Marker}E{index}{k_Marker}";

    public static NotebookProgram Build(IReadOnlyList<NotebookProgramCell> cells)
        => Build(cells, CancellationToken.None);

    internal static NotebookProgram Build(IReadOnlyList<NotebookProgramCell> cells, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var ranges = new List<CellRange>(cells.Count);
        var sources = new List<CSharpSourceDocument>();
        var mappings = new List<CSharpSourceMapping>();
        var line = 0;

        void Emit(string text)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                return (File: file, Path: path, Parts: Split(file.Source, cancellationToken));
            }).ToArray())).ToArray();
        var emittedUsings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, files) in splitCells) {
            foreach (var (file, _, parts) in files) {
                foreach (var usingDirective in parts.RunnerUsings) {
                    if (emittedUsings.Add(usingDirective.Text)) {
                        mappings.Add(new("Notebook/Runner.g.cs", builder.Length, file.Id, usingDirective.Start, usingDirective.Text.Length));
                        Emit(EnsureTrailingNewline(usingDirective.Text));
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
            foreach (var (file, path, parts) in files) {
                if (parts.Statements.Length == 0) {
                    if (parts.RunnerUsings.Count == 0 || parts.RunnerUsings[^1].Text.EndsWith(';'))
                        mappings.Add(new("Notebook/Runner.g.cs", builder.Length, file.Id, parts.Usings.Length, 0));
                    continue;
                }
                Emit($"#line {parts.StatementsStartLine} \"{EscapeDirectivePath(path)}\"\n");
                mappings.Add(new("Notebook/Runner.g.cs", builder.Length, file.Id, parts.Usings.Length, parts.Statements.Length));
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
                    BuildFileSource(file.Id, path, parts, mappings)));
            }
        }

        return new NotebookProgram(builder.ToString(), true, ranges, sources, mappings);
    }

    private static string BuildFileSource(string id, string path, SourceParts parts, List<CSharpSourceMapping> mappings)
    {
        var builder = new StringBuilder();
        mappings.Add(new(path, 0, id, 0, parts.Usings.Length));
        builder.Append(EnsureTrailingNewline(parts.Usings));
        if (parts.Types.Length > 0) {
            builder.Append("#line ").Append(parts.TypesStartLine)
                .Append(" \"").Append(EscapeDirectivePath(path)).Append("\"\n");
            mappings.Add(new(path, builder.Length, id, parts.Usings.Length + parts.Statements.Length, parts.Types.Length));
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

    private static SourceParts Split(string source, CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var root = (CompilationUnitSyntax)tree.GetRoot(cancellationToken);
        var text = tree.GetText(cancellationToken);

        var usingsEnd = root.Usings.Count > 0 ? root.Usings[^1].FullSpan.End : 0;
        var runnerUsings = root.Usings
            .Select(static usingDirective => new RunnerUsing(usingDirective.WithoutTrivia().ToFullString(), usingDirective.SpanStart))
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
        IReadOnlyList<RunnerUsing> RunnerUsings);

    private readonly record struct RunnerUsing(string Text, int Start);
}
