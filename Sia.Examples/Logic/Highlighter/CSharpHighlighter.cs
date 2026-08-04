using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Sia_Examples.Notebook;

public readonly record struct HighlightRun(int Start, int Length, string Classification);

public static class CSharpHighlighter
{
    public static async Task<IReadOnlyList<HighlightRun>> ClassifyAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId, "Highlight.cs");

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "NotebookHighlight", "NotebookHighlight", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, CSharpParseOptions.Default)
            .AddDocument(documentId, "Highlight.cs", SourceText.From(source));
        workspace.TryApplyChanges(solution);

        var document = workspace.CurrentSolution.GetDocument(documentId)!;
        var spans = await Classifier.GetClassifiedSpansAsync(document, new TextSpan(0, source.Length))
            .ConfigureAwait(false);
        return Reconcile(spans);
    }

    private static IReadOnlyList<HighlightRun> Reconcile(IEnumerable<ClassifiedSpan> spans)
    {
        var bySpan = spans
            .Where(s => s.ClassificationType != ClassificationTypeNames.Text && s.TextSpan.Length > 0)
            .GroupBy(s => s.TextSpan)
            .Select(g => new HighlightRun(
                g.Key.Start, g.Key.Length, g.MaxBy(s => Priority(s.ClassificationType)).ClassificationType))
            .OrderBy(r => r.Start)
            .ThenByDescending(r => r.Length);

        var result = new List<HighlightRun>();
        var cursor = 0;
        foreach (var run in bySpan) {
            if (run.Start < cursor) {
                continue;
            }
            result.Add(run);
            cursor = run.Start + run.Length;
        }
        return result;
    }

    private static int Priority(string classification) => classification switch {
        ClassificationTypeNames.Identifier => 0,
        ClassificationTypeNames.Punctuation or ClassificationTypeNames.Operator => 1,
        _ => 2,
    };

    public static string ToAnsi(string source, IReadOnlyList<HighlightRun> runs)
    {
        var builder = new StringBuilder(source.Length + runs.Count * 12);
        var cursor = 0;
        foreach (var run in runs.OrderBy(r => r.Start)) {
            if (run.Start > cursor) {
                builder.Append(source, cursor, run.Start - cursor);
            }
            var color = AnsiColor(run.Classification);
            if (color is { } code) {
                builder.Append("\e[38;5;").Append(code).Append('m');
                builder.Append(source, run.Start, run.Length);
                builder.Append("\e[0m");
            }
            else {
                builder.Append(source, run.Start, run.Length);
            }
            cursor = run.Start + run.Length;
        }
        if (cursor < source.Length) {
            builder.Append(source, cursor, source.Length - cursor);
        }
        return builder.ToString();
    }

    public static string CssClass(string classification) => "tok-" + classification
        .Replace(" ", "-", StringComparison.Ordinal)
        .ToLowerInvariant();

    private static int? AnsiColor(string classification) => classification switch {
        ClassificationTypeNames.Keyword
            or ClassificationTypeNames.ControlKeyword
            or ClassificationTypeNames.PreprocessorKeyword => 183,
        ClassificationTypeNames.StringLiteral
            or ClassificationTypeNames.VerbatimStringLiteral
            or ClassificationTypeNames.StringEscapeCharacter => 150,
        ClassificationTypeNames.Comment => 103,
        ClassificationTypeNames.NumericLiteral => 216,
        ClassificationTypeNames.ClassName
            or ClassificationTypeNames.StructName
            or ClassificationTypeNames.InterfaceName
            or ClassificationTypeNames.EnumName
            or ClassificationTypeNames.DelegateName
            or ClassificationTypeNames.RecordClassName
            or ClassificationTypeNames.RecordStructName
            or ClassificationTypeNames.TypeParameterName => 223,
        ClassificationTypeNames.MethodName
            or ClassificationTypeNames.ExtensionMethodName => 111,
        ClassificationTypeNames.PropertyName
            or ClassificationTypeNames.FieldName
            or ClassificationTypeNames.ParameterName
            or ClassificationTypeNames.LocalName => 253,
        ClassificationTypeNames.Operator or ClassificationTypeNames.Punctuation => 145,
        ClassificationTypeNames.Identifier => 253,
        _ => null,
    };
}
