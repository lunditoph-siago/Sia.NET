using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Sia.Examples.Notebook;

namespace Sia.Examples.Editor;

internal sealed record DocumentSymbol(string Name, string Kind, string Container, SourcePosition Location);

internal static class CSharpDocumentSymbols
{
    internal static DocumentSymbol[] Read(IEnumerable<CSharpSourceDocument> documents, CancellationToken cancellation)
    {
        var symbols = new List<DocumentSymbol>();
        foreach (var document in documents) {
            cancellation.ThrowIfCancellationRequested();
            var text = SourceText.From(document.Source);
            var tree = CSharpSyntaxTree.ParseText(text, CSharpLanguageOptions.Parse, document.Path, cancellationToken: cancellation);
            foreach (var node in tree.GetRoot(cancellation).DescendantNodes()) {
                cancellation.ThrowIfCancellationRequested();
                var (name, kind, span) = node switch {
                    BaseNamespaceDeclarationSyntax space => (space.Name.ToString(), "namespace", space.Name.Span),
                    BaseTypeDeclarationSyntax type => (TypeName(type), type.KeywordName(), type.Identifier.Span),
                    DelegateDeclarationSyntax type => (type.Identifier.ValueText + type.TypeParameterList + Parameters(type.ParameterList), "delegate", type.Identifier.Span),
                    MethodDeclarationSyntax method => (method.ExplicitInterfaceSpecifier + method.Identifier.ValueText + method.TypeParameterList + Parameters(method.ParameterList), "method", method.Identifier.Span),
                    ConstructorDeclarationSyntax ctor => (ctor.Identifier.ValueText + Parameters(ctor.ParameterList), "constructor", ctor.Identifier.Span),
                    DestructorDeclarationSyntax dtor => ("~" + dtor.Identifier.ValueText + Parameters(dtor.ParameterList), "destructor", dtor.Identifier.Span),
                    OperatorDeclarationSyntax op => ("operator " + op.OperatorToken.Text + Parameters(op.ParameterList), "operator", op.OperatorToken.Span),
                    ConversionOperatorDeclarationSyntax op => (op.ImplicitOrExplicitKeyword.Text + " operator " + op.Type + Parameters(op.ParameterList), "operator", op.Type.Span),
                    PropertyDeclarationSyntax property => (property.ExplicitInterfaceSpecifier + property.Identifier.ValueText, "property", property.Identifier.Span),
                    IndexerDeclarationSyntax indexer => (indexer.ExplicitInterfaceSpecifier + "this" + Parameters(indexer.ParameterList), "indexer", indexer.ThisKeyword.Span),
                    EventDeclarationSyntax item => (item.ExplicitInterfaceSpecifier + item.Identifier.ValueText, "event", item.Identifier.Span),
                    VariableDeclaratorSyntax variable when variable.Parent?.Parent is BaseFieldDeclarationSyntax field
                        => (variable.Identifier.ValueText, field is EventFieldDeclarationSyntax ? "event" : "field", variable.Identifier.Span),
                    EnumMemberDeclarationSyntax member => (member.Identifier.ValueText, "enum member", member.Identifier.Span),
                    LocalFunctionStatementSyntax local => (local.Identifier.ValueText + local.TypeParameterList + Parameters(local.ParameterList), "local function", local.Identifier.Span),
                    ParameterSyntax parameter when parameter.Parent?.Parent is RecordDeclarationSyntax
                        => (parameter.Identifier.ValueText, "property", parameter.Identifier.Span),
                    _ => ("", "", default(TextSpan))
                };
                if (name.Length == 0 || span.Length == 0) continue;
                var point = text.Lines.GetLinePosition(span.Start);
                var container = string.Join('.', node.Ancestors().Reverse().Select(static parent => parent switch {
                    BaseNamespaceDeclarationSyntax space => space.Name.ToString(),
                    BaseTypeDeclarationSyntax type => TypeName(type),
                    MethodDeclarationSyntax method => method.Identifier.ValueText,
                    LocalFunctionStatementSyntax local => local.Identifier.ValueText,
                    _ => null
                }).Where(static part => part is not null));
                symbols.Add(new(name, kind, container, new(document.Id, document.DisplayPath, span.Start, span.Length,
                    point.Line + 1, point.Character + 1, document.Source, false)));
            }
        }
        return [.. symbols];
    }

    private static string TypeName(BaseTypeDeclarationSyntax type)
        => type.Identifier.ValueText + (type as TypeDeclarationSyntax)?.TypeParameterList;

    private static string KeywordName(this BaseTypeDeclarationSyntax type)
        => type is RecordDeclarationSyntax record ? "record" : type switch {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            _ => "enum"
        };

    private static string Parameters(BaseParameterListSyntax list)
        => (list is BracketedParameterListSyntax ? "[" : "(")
            + string.Join(", ", list.Parameters.Select(static parameter =>
                string.Join(' ', parameter.Modifiers.Select(static token => token.Text)
                    .Concat(new[] { parameter.Type?.WithoutTrivia().ToString(), parameter.Identifier.ValueText })
                    .Where(static part => !string.IsNullOrEmpty(part)))))
            + (list is BracketedParameterListSyntax ? "]" : ")");
}
