using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Sia.Examples.Editor;

internal sealed record SymbolOccurrence(SourcePosition Location, string Symbol, bool Definition, bool Implicit, bool Candidate);
internal sealed record SymbolReferences(string Display, IReadOnlyList<SymbolOccurrence> Occurrences);

internal static class CSharpSymbolReferences
{
    internal static async Task<SymbolReferences?> QueryAsync(Document document, int position,
        Func<Document, string?> documentKey, CancellationToken cancellation, ISymbol? selected = null)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellation);
        var tree = await document.GetSyntaxTreeAsync(cancellation);
        if (compilation is null || tree is null) return null;
        var workspace = document.Project.Solution.Workspace;
        var model = compilation.GetSemanticModel(tree);
        var symbol = selected ?? await SymbolFinder.FindSymbolAtPositionAsync(model, position, workspace, cancellation);
        if (symbol is null) return null;
        symbol = Canonical(symbol);
        var family = Family(symbol, compilation, cancellation);
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (symbol is ITypeSymbol) names.Add("var");
        foreach (var member in family) {
            var name = member.Name[(member.Name.LastIndexOf('.') + 1)..];
            names.Add(name);
            if (member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor } method)
                names.Add(method.ContainingType.Name);
            if (member is INamedTypeSymbol type && type.Name.EndsWith("Attribute", StringComparison.Ordinal)) names.Add(type.Name[..^9]);
        }
        var documents = document.Project.Documents.Concat(await document.Project.GetSourceGeneratedDocumentsAsync(cancellation));
        var files = new Dictionary<SyntaxTree, SearchDocument>();
        foreach (var file in documents) {
            cancellation.ThrowIfCancellationRequested();
            var syntax = await file.GetSyntaxTreeAsync(cancellation);
            if (syntax is null) continue;
            var root = await syntax.GetRootAsync(cancellation);
            files.Add(syntax, new(file, root, await file.GetTextAsync(cancellation), compilation.GetSemanticModel(syntax)));
            foreach (var alias in root.DescendantNodes().OfType<UsingDirectiveSyntax>()) {
                if (alias.Alias is { } name) names.Add(name.Name.Identifier.ValueText);
            }
        }
        var results = new Dictionary<(DocumentId, int, int), SymbolOccurrence>();
        foreach (var member in family) {
            foreach (var location in member.Locations) {
                if (location.SourceTree is not { } source || !files.TryGetValue(source, out var file)) continue;
                if ((member.IsImplicitlyDeclared || member.ContainingSymbol?.IsImplicitlyDeclared == true)
                    && file.Root.FindToken(location.SourceSpan.Start).ValueText != member.Name) continue;
                Add(file, location.SourceSpan, member, definition: true, implicitReference: member.IsImplicitlyDeclared);
            }
        }
        foreach (var file in files.Values) {
            foreach (var token in file.Root.DescendantTokens(descendIntoTrivia: true)) {
                cancellation.ThrowIfCancellationRequested();
                if (!(token.IsKind(SyntaxKind.IdentifierToken) && names.Contains(token.ValueText))
                    && !(symbol is INamedTypeSymbol { SpecialType: not SpecialType.None } && token.Parent is PredefinedTypeSyntax)) continue;
                var bound = await SymbolFinder.FindSymbolAtPositionAsync(file.Model, token.SpanStart, workspace, cancellation);
                if (token.Parent is SimpleNameSyntax expression && file.Model.GetSymbolInfo(expression, cancellation).Symbol is { } expressionSymbol
                    && Matches(expressionSymbol)) bound = expressionSymbol;
                if (token.Parent is IdentifierNameSyntax name && file.Model.GetAliasInfo(name, cancellation) is { } alias)
                    bound = symbol is IAliasSymbol ? alias : alias.Target;
                var aliasDeclaration = token.Parent?.Parent is NameEqualsSyntax { Parent: UsingDirectiveSyntax };
                if (bound is not null && Matches(bound) && (!aliasDeclaration || symbol is IAliasSymbol))
                    Add(file, token.Span, bound, implicitReference: token.ValueText == "var" && symbol is ITypeSymbol && bound.Name != "var");
                else if (token.Parent is SimpleNameSyntax simple && !aliasDeclaration) {
                    var info = file.Model.GetSymbolInfo(simple, cancellation);
                    var nameOf = simple.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault(invocation =>
                        invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
                    var exactGroup = nameOf is not null && file.Model.GetOperation(nameOf, cancellation) is INameOfOperation;
                    foreach (var candidate in info.CandidateSymbols.Where(Matches)) Add(file, token.Span, candidate, candidate: !exactGroup);
                }
            }
            if (!family.Any(static member => member is IMethodSymbol or IPropertySymbol or IFieldSymbol)) continue;
            var operations = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
            foreach (var node in file.Root.DescendantNodesAndSelf()) {
                cancellation.ThrowIfCancellationRequested();
                if (node is AwaitExpressionSyntax awaitExpression) {
                    var info = file.Model.GetAwaitExpressionInfo(awaitExpression);
                    foreach (var member in new ISymbol?[] { info.GetAwaiterMethod, info.GetResultMethod, info.IsCompletedProperty }) {
                        if (member is not null && Matches(member)) Add(file, awaitExpression.AwaitKeyword.Span, member, implicitReference: true);
                    }
                }
                if (node is not (BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or GlobalStatementSyntax
                    or ArrowExpressionClauseSyntax or EqualsValueClauseSyntax or AttributeSyntax)) continue;
                var operation = file.Model.GetOperation(node, cancellation);
                if (operation is null) continue;
                while (operation.Parent is { } parent) operation = parent;
                operations.Add(operation);
            }
            foreach (var operation in operations) {
                Walk(operation, file, null);
                if (operation is IBlockOperation or IMethodBodyOperation or IConstructorBodyOperation or IFieldInitializerOperation
                    or IPropertyInitializerOperation or IParameterInitializerOperation or IAttributeOperation) {
                    if (ControlFlowGraph.Create(operation.Syntax, file.Model, cancellation) is { } graph) WalkGraph(graph, file);
                }
            }
        }
        return new(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), results.Values
            .OrderByDescending(static occurrence => occurrence.Definition).ThenBy(static occurrence => occurrence.Location.Path, StringComparer.Ordinal)
            .ThenBy(static occurrence => occurrence.Location.Start).ThenBy(static occurrence => occurrence.Implicit).ToArray());

        bool Matches(ISymbol member) => family.Contains(Canonical(member));

        void Add(SearchDocument file, TextSpan span, ISymbol member, bool definition = false, bool implicitReference = false, bool candidate = false)
        {
            if (span.End > file.Text.Length) return;
            var point = file.Text.Lines.GetLinePosition(span.Start);
            var id = documentKey(file.Document);
            var location = new SourcePosition(id ?? "$generated:" + file.Document.Id, file.Document.FilePath ?? file.Document.Name,
                span.Start, span.Length, point.Line + 1, point.Character + 1, file.Text.ToString(), id is null);
            var key = (file.Document.Id, span.Start, span.Length);
            if (results.TryGetValue(key, out var previous)) {
                results[key] = previous with {
                    Definition = previous.Definition || definition,
                    Implicit = previous.Implicit && implicitReference,
                    Candidate = previous.Candidate && candidate
                };
            }
            else results.Add(key, new(location, member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), definition, implicitReference, candidate));
        }

        void WalkGraph(ControlFlowGraph graph, SearchDocument file)
        {
            foreach (var block in graph.Blocks) {
                foreach (var operation in block.Operations) Walk(operation, file, graph);
                if (block.BranchValue is { } branch) Walk(branch, file, graph);
            }
            foreach (var local in graph.LocalFunctions) WalkGraph(graph.GetLocalFunctionControlFlowGraph(local, cancellation), file);
        }

        void Walk(IOperation operation, SearchDocument file, ControlFlowGraph? graph)
        {
            var pending = new Stack<IOperation>();
            pending.Push(operation);
            while (pending.TryPop(out var current)) {
                cancellation.ThrowIfCancellationRequested();
                if (current is IDynamicMemberReferenceOperation dynamicMember && family.Any(member => member.Name == dynamicMember.MemberName))
                    Add(file, ReferenceSpan(current.Syntax, false), symbol, candidate: true);
                var member = current switch {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IMethodReferenceOperation method => method.Method,
                    IObjectCreationOperation creation => creation.Constructor,
                    IMemberReferenceOperation reference => reference.Member,
                    IConversionOperation conversion => conversion.OperatorMethod,
                    IBinaryOperation binary => binary.OperatorMethod,
                    IUnaryOperation unary => unary.OperatorMethod,
                    IIncrementOrDecrementOperation increment => increment.OperatorMethod,
                    ICompoundAssignmentOperation assignment => assignment.OperatorMethod,
                    _ => null
                };
                if (member is not null && Matches(member)) Add(file, ReferenceSpan(current.Syntax, current.IsImplicit), member,
                    implicitReference: current.IsImplicit || current.Syntax is ImplicitObjectCreationExpressionSyntax);
                if (graph is not null && current is IFlowAnonymousFunctionOperation anonymous)
                    WalkGraph(graph.GetAnonymousFunctionControlFlowGraph(anonymous, cancellation), file);
                foreach (var child in current.ChildOperations) pending.Push(child);
            }
        }
    }

    private static TextSpan ReferenceSpan(SyntaxNode syntax, bool implicitReference)
    {
        if (implicitReference) {
            foreach (var parent in syntax.AncestorsAndSelf()) {
                if (parent is CommonForEachStatementSyntax loop) return loop.ForEachKeyword.Span;
                if (parent is UsingStatementSyntax statement) return statement.UsingKeyword.Span;
                if (parent is LocalDeclarationStatementSyntax local && !local.UsingKeyword.IsKind(SyntaxKind.None)) return local.UsingKeyword.Span;
            }
        }
        return syntax switch {
            InvocationExpressionSyntax invocation => ReferenceSpan(invocation.Expression, false),
            ObjectCreationExpressionSyntax creation => ReferenceSpan(creation.Type, false),
            AttributeSyntax attribute => ReferenceSpan(attribute.Name, false),
            ImplicitObjectCreationExpressionSyntax creation => creation.NewKeyword.Span,
            ConstructorInitializerSyntax initializer => initializer.ThisOrBaseKeyword.Span,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Span,
            MemberBindingExpressionSyntax member => member.Name.Identifier.Span,
            SimpleNameSyntax name => name.Identifier.Span,
            QualifiedNameSyntax name => ReferenceSpan(name.Right, false),
            AliasQualifiedNameSyntax name => name.Name.Identifier.Span,
            PredefinedTypeSyntax type => type.Keyword.Span,
            BinaryExpressionSyntax binary => binary.OperatorToken.Span,
            AssignmentExpressionSyntax assignment => assignment.OperatorToken.Span,
            PrefixUnaryExpressionSyntax unary => unary.OperatorToken.Span,
            PostfixUnaryExpressionSyntax unary => unary.OperatorToken.Span,
            ElementAccessExpressionSyntax access => access.ArgumentList.OpenBracketToken.Span,
            _ => syntax.Span
        };
    }

    private static ISymbol Canonical(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;
        if (symbol is IMethodSymbol { AssociatedSymbol: { } associated }) symbol = associated;
        return symbol.OriginalDefinition;
    }

    private static HashSet<ISymbol> Family(ISymbol symbol, Compilation compilation, CancellationToken cancellation)
    {
        var result = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol };
        if (symbol is IParameterSymbol sourceParameter && sourceParameter.ContainingSymbol is IMethodSymbol owner) {
            foreach (var part in new[] { owner.PartialDefinitionPart, owner.PartialImplementationPart }) {
                if (part is not null && sourceParameter.Ordinal < part.Parameters.Length) result.Add(Canonical(part.Parameters[sourceParameter.Ordinal]));
            }
        }
        if (symbol.ContainingType is { IsRecord: true } record && symbol is IParameterSymbol or IPropertySymbol) {
            foreach (var parameter in record.InstanceConstructors.SelectMany(static constructor => constructor.Parameters)
                .Where(parameter => parameter.Name == symbol.Name && parameter.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax(cancellation) is ParameterSyntax { Parent: ParameterListSyntax { Parent: RecordDeclarationSyntax } }))) {
                result.Add(Canonical(parameter));
                foreach (var property in record.GetMembers(parameter.Name).OfType<IPropertySymbol>()) result.Add(Canonical(property));
                foreach (var deconstruct in record.GetMembers("Deconstruct").OfType<IMethodSymbol>().Where(static method => method.IsImplicitlyDeclared)) {
                    if (parameter.Ordinal < deconstruct.Parameters.Length) result.Add(Canonical(deconstruct.Parameters[parameter.Ordinal]));
                }
            }
        }
        if (symbol is INamedTypeSymbol type) {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(static method =>
                method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor)) result.Add(Canonical(method));
            return result;
        }
        if (!result.Any(static member => member is IMethodSymbol or IPropertySymbol or IEventSymbol)) return result;
        var edges = new Dictionary<ISymbol, HashSet<ISymbol>>(SymbolEqualityComparer.Default);
        foreach (var sourceType in Types(compilation.Assembly.GlobalNamespace, cancellation)) {
            cancellation.ThrowIfCancellationRequested();
            foreach (var member in sourceType.GetMembers()) {
                switch (member) {
                    case IMethodSymbol method:
                        Link(method, method.OverriddenMethod); Link(method, method.PartialDefinitionPart); Link(method, method.PartialImplementationPart);
                        foreach (var contract in method.ExplicitInterfaceImplementations) Link(method, contract);
                        break;
                    case IPropertySymbol property:
                        Link(property, property.OverriddenProperty);
                        foreach (var contract in property.ExplicitInterfaceImplementations) Link(property, contract);
                        break;
                    case IEventSymbol @event:
                        Link(@event, @event.OverriddenEvent);
                        foreach (var contract in @event.ExplicitInterfaceImplementations) Link(@event, contract);
                        break;
                }
            }
            foreach (var contract in sourceType.AllInterfaces.SelectMany(static type => type.GetMembers())) {
                cancellation.ThrowIfCancellationRequested();
                Link(contract, sourceType.FindImplementationForInterfaceMember(contract));
            }
        }
        var pending = new Queue<ISymbol>(result);
        while (pending.TryDequeue(out var member)) {
            if (!edges.TryGetValue(member, out var related)) continue;
            foreach (var next in related) if (result.Add(next)) pending.Enqueue(next);
        }
        return result;

        void Link(ISymbol left, ISymbol? right)
        {
            if (right is null) return;
            left = Canonical(left); right = Canonical(right);
            if (!edges.TryGetValue(left, out var outgoing)) edges.Add(left, outgoing = new(SymbolEqualityComparer.Default));
            if (!edges.TryGetValue(right, out var incoming)) edges.Add(right, incoming = new(SymbolEqualityComparer.Default));
            outgoing.Add(right); incoming.Add(left);
        }
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol scope, CancellationToken cancellation)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(scope);
        while (pending.TryPop(out var current)) {
            cancellation.ThrowIfCancellationRequested();
            foreach (var member in current.GetMembers()) {
                if (member is INamedTypeSymbol type) { pending.Push(type); yield return type; }
                else if (member is INamespaceSymbol space) pending.Push(space);
            }
        }
    }

    private sealed record SearchDocument(Document Document, SyntaxNode Root, SourceText Text, SemanticModel Model);
}
