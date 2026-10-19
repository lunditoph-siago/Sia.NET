using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace Sia.Examples.Editor;

internal static class CSharpRename
{
    internal static async Task<RenameTarget> TargetAsync(Document document, int position, CancellationToken cancellation)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellation);
        var tree = await document.GetSyntaxTreeAsync(cancellation);
        if (compilation is null || tree is null) return new("", "The C# project is unavailable.");
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(compilation.GetSemanticModel(tree), position,
            document.Project.Solution.Workspace, cancellation);
        symbol = RenameSymbol(symbol);
        if (symbol is null) return new("", "There is no symbol at the cursor.");
        return new(symbol.Name[(symbol.Name.LastIndexOf('.') + 1)..]);
    }

    private static ISymbol? RenameSymbol(ISymbol? symbol)
        => symbol switch {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor } constructor => constructor.ContainingType,
            IMethodSymbol { AssociatedSymbol: { } associated } => associated,
            _ => symbol
        };

    internal static async Task<RenamePlan> PrepareAsync(Document document, int position, string name,
        Func<Document, string?> documentKey, CSharpProjectSnapshot snapshot, CancellationToken cancellation)
    {
        var conflicts = new List<RenameConflict>();
        var context = snapshot.Projection?.Sources ?? snapshot.Documents;
        RenamePlan Failure(string message) => new("", name, [], [new(message)], context, snapshot.ActiveDocumentId);
        var value = name.StartsWith('@') ? name[1..] : name;
        var nameToken = SyntaxFactory.ParseToken("@" + value);
        if (value.Length == 0 || !nameToken.IsKind(SyntaxKind.IdentifierToken) || nameToken.ContainsDiagnostics
            || nameToken.Text != "@" + value || nameToken.ValueText != value) return Failure("Enter a valid C# identifier.");
        var replacement = SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
            ? "@" + value : value;
        var compilation = await document.Project.GetCompilationAsync(cancellation);
        if (compilation is null) return Failure("The C# project is unavailable.");
        var tree = await document.GetSyntaxTreeAsync(cancellation);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(compilation.GetSemanticModel(tree!), position,
            document.Project.Solution.Workspace, cancellation);
        symbol = RenameSymbol(symbol);
        if (symbol is null) return Failure("There is no symbol at the cursor.");
        if (symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion }
            or IPropertySymbol { IsIndexer: true }) return Failure("This member does not have a renameable identifier.");
        var references = await CSharpSymbolReferences.QueryAsync(document, position, documentKey, cancellation, symbol);
        if (references is null) return Failure("The symbol's references are unavailable.");
        SourcePosition Original(SourcePosition location) => snapshot.Projection?.ToSource(location) ?? location;
        bool Editable(SourcePosition location) => !location.Generated && context.Any(source => source.Id == location.DocumentId && source.IsUserCode);
        var declarations = references.Occurrences.Where(static occurrence => occurrence.Definition).ToArray();
        if (declarations.Length == 0 || !declarations.Any(occurrence => Editable(Original(occurrence.Location))))
            return Failure("The declaration is in metadata or generated code and cannot be edited.");
        if (symbol.Name[(symbol.Name.LastIndexOf('.') + 1)..] == value) return new(references.Display, value, [], [], context, snapshot.ActiveDocumentId);
        var changes = new Dictionary<string, List<TextChange>>(StringComparer.Ordinal);
        foreach (var occurrence in references.Occurrences) {
            cancellation.ThrowIfCancellationRequested();
            if (occurrence.Candidate) { conflicts.Add(new("Resolve this possible reference before renaming.", Original(occurrence.Location))); continue; }
            if (occurrence.Implicit) continue;
            var original = Original(occurrence.Location);
            if (!Editable(original)) { conflicts.Add(new("This reference belongs to generated or read-only code.", original)); continue; }
            var location = occurrence.Location;
            var source = document.Project.Documents.FirstOrDefault(file => file.FilePath == location.Path);
            if (source is null) { conflicts.Add(new("The source document is no longer available.", original)); continue; }
            var root = await source.GetSyntaxRootAsync(cancellation);
            var token = root!.FindToken(location.Start, findInsideTrivia: true);
            if (!token.IsKind(SyntaxKind.IdentifierToken) || token.SpanStart != location.Start || token.Span.Length != location.Length) {
                conflicts.Add(new("This occurrence is not an editable identifier.", original)); continue;
            }
            var text = replacement;
            if (symbol is INamedTypeSymbol && symbol.Name.EndsWith("Attribute", StringComparison.Ordinal)
                && token.ValueText == symbol.Name[..^9] && value.EndsWith("Attribute", StringComparison.Ordinal)) {
                var shortName = value[..^9];
                if (shortName.Length > 0) text = Escape(shortName);
            }
            if (token.Parent is IdentifierNameSyntax identifier) {
                if (identifier.Parent is AnonymousObjectMemberDeclaratorSyntax { NameEquals: null }) text = Escape(token.ValueText) + " = " + text;
                else if (identifier.Parent is ArgumentSyntax { NameColon: null, Parent: TupleExpressionSyntax }) text = Escape(token.ValueText) + ": " + text;
                else if (identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier) {
                    var prefix = access.Parent switch {
                        AnonymousObjectMemberDeclaratorSyntax { NameEquals: null } => Escape(token.ValueText) + " = ",
                        ArgumentSyntax { NameColon: null, Parent: TupleExpressionSyntax } => Escape(token.ValueText) + ": ",
                        _ => null
                    };
                    if (prefix is not null) {
                        if (!changes.TryGetValue(location.Path, out var additions)) changes.Add(location.Path, additions = []);
                        var addition = new TextChange(new(access.SpanStart, 0), prefix);
                        if (!additions.Contains(addition)) additions.Add(addition);
                    }
                }
            }
            if (!changes.TryGetValue(location.Path, out var edits)) changes.Add(location.Path, edits = []);
            var edit = new TextChange(token.Span, text);
            if (!edits.Contains(edit)) edits.Add(edit);
        }
        var documents = new List<RenameDocumentEdit>();
        foreach (var (path, edits) in changes) {
            var source = document.Project.Documents.First(file => file.FilePath == path);
            var text = await source.GetTextAsync(cancellation);
            foreach (var edit in edits) {
                var point = text.Lines.GetLinePosition(edit.Span.Start);
                var location = Original(new(documentKey(source)!, path, edit.Span.Start, edit.Span.Length,
                    point.Line + 1, point.Character + 1, text.ToString(), false));
                var existing = documents.FindIndex(item => item.DocumentId == location.DocumentId);
                var change = new ChangeSpec(location.Start, location.Start + location.Length, edit.NewText!);
                if (existing < 0) documents.Add(new(location.DocumentId, location.Path, location.Source, [change]));
                else documents[existing] = documents[existing] with { Changes = [.. documents[existing].Changes, change] };
            }
        }
        for (var index = 0; index < documents.Count; index++) documents[index] = documents[index] with {
            Changes = documents[index].Changes.OrderBy(static edit => edit.From).ToArray()
        };
        if (conflicts.Count == 0) await ValidateAsync(document.Project, compilation, changes, conflicts, cancellation);
        return new(references.Display, value, documents, conflicts, context, snapshot.ActiveDocumentId);
    }

    private static string Escape(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static async Task ValidateAsync(Project project, Compilation before, Dictionary<string, List<TextChange>> changes,
        List<RenameConflict> conflicts, CancellationToken cancellation)
    {
        var updated = project.Solution;
        var files = new Dictionary<SyntaxTree, (Document Document, SyntaxNode Root, SourceText Text, TextChange[] Edits)>();
        foreach (var document in project.Documents) {
            cancellation.ThrowIfCancellationRequested();
            var text = await document.GetTextAsync(cancellation);
            var tree = (await document.GetSyntaxTreeAsync(cancellation))!;
            var edits = changes.TryGetValue(document.FilePath!, out var values) ? values.OrderBy(static edit => edit.Span.Start).ToArray() : [];
            files.Add(tree, (document, (await tree.GetRootAsync(cancellation)), text, edits));
            if (edits.Length > 0) updated = updated.WithDocumentText(document.Id, text.WithChanges(edits));
        }
        var after = (await updated.GetProject(project.Id)!.GetCompilationAsync(cancellation))!;
        var originalErrors = before.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .GroupBy(static diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage()).ToDictionary(static group => group.Key, static group => group.Count());
        foreach (var diagnostic in after.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) {
            var key = diagnostic.Id + ":" + diagnostic.GetMessage();
            if (originalErrors.TryGetValue(key, out var count) && count > 0) originalErrors[key] = count - 1;
            else conflicts.Add(new($"{diagnostic.Id}: {diagnostic.GetMessage()}"));
        }
        if (conflicts.Count > 0) return;
        var mappedTrees = new Dictionary<SyntaxTree, SyntaxTree>();
        foreach (var (tree, file) in files) mappedTrees.Add(tree, (await updated.GetDocument(file.Document.Id)!.GetSyntaxTreeAsync(cancellation))!);
        foreach (var (tree, file) in files) {
            var oldModel = before.GetSemanticModel(tree);
            var newModel = after.GetSemanticModel(mappedTrees[tree]);
            foreach (var token in file.Root.DescendantTokens(descendIntoTrivia: true).Where(static token => token.IsKind(SyntaxKind.IdentifierToken))) {
                cancellation.ThrowIfCancellationRequested();
                var oldSymbol = await BoundSymbolAsync(oldModel, token.SpanStart);
                if (oldSymbol is null) continue;
                var offset = Map(file.Edits, token.SpanStart);
                var edit = file.Edits.FirstOrDefault(edit => edit.Span == token.Span);
                if (edit.NewText is { } inserted && (inserted.Contains(" = ", StringComparison.Ordinal) || inserted.Contains(": ", StringComparison.Ordinal)))
                    offset += inserted.LastIndexOf(' ') + 1;
                var newSymbol = await BoundSymbolAsync(newModel, offset);
                if (Identity(oldSymbol, old: true) == Identity(newSymbol, old: false)) continue;
                conflicts.Add(new($"Renaming would change what '{token.ValueText}' refers to."));
                if (conflicts.Count >= 20) return;
            }
        }

        async Task<ISymbol?> BoundSymbolAsync(SemanticModel model, int position)
        {
            var token = (await model.SyntaxTree.GetRootAsync(cancellation)).FindToken(position, findInsideTrivia: true);
            return token.Parent is SimpleNameSyntax name && model.GetSymbolInfo(name, cancellation).Symbol is { } symbol ? symbol
                : await SymbolFinder.FindSymbolAtPositionAsync(model, position, project.Solution.Workspace, cancellation);
        }

        string Identity(ISymbol? symbol, bool old)
        {
            if (symbol is null) return "$unbound";
            if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;
            if (symbol is IMethodSymbol { AssociatedSymbol: { } associated }) symbol = associated;
            symbol = symbol.OriginalDefinition;
            var locations = symbol.Locations.Where(static location => location.IsInSource).Select(location => {
                var sourceTree = location.SourceTree!;
                if (old && files.TryGetValue(sourceTree, out var source)) return source.Document.Id + ":" + Map(source.Edits, location.SourceSpan.Start);
                if (!old) {
                    foreach (var (original, mapped) in mappedTrees) if (mapped == sourceTree) return files[original].Document.Id + ":" + location.SourceSpan.Start;
                }
                return sourceTree.FilePath + ":" + location.SourceSpan.Start;
            }).Order(StringComparer.Ordinal).ToArray();
            if (symbol is IPropertySymbol { ContainingType.IsAnonymousType: true } property)
                return "anonymous:" + property.Name + ":" + Identity(property.Type, old);
            if (symbol is IFieldSymbol { ContainingType.IsTupleType: true } tuple)
                return "tuple:" + tuple.Name + ":" + Identity(tuple.Type, old);
            return locations.Length > 0 ? symbol.Kind + ":" + (symbol is IMethodSymbol method ? method.MethodKind.ToString() : "") + ":" + string.Join(';', locations)
                : symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }
    }

    private static int Map(IEnumerable<TextChange> changes, int position)
    {
        var offset = 0;
        foreach (var change in changes) {
            if (change.Span.Start > position || change.Span.Start == position && change.Span.Length > 0) break;
            offset += (change.NewText?.Length ?? 0) - change.Span.Length;
        }
        return position + offset;
    }
}
