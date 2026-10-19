using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sia_Examples.Editor;

internal sealed record SignatureHelp(int Start, IReadOnlyList<SignatureItem> Items, int Selected);
internal sealed record SignatureItem(string Prefix, string[] Parameters, string Suffix, int ActiveParameter,
    string Documentation, string ParameterDocumentation, bool Applicable);

internal static class CSharpSignatureHelp
{
    private static readonly SymbolDisplayFormat _display = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeType)
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeOptionalBrackets);

    internal static async Task<SignatureHelp?> QueryAsync(Document document, int position, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation);
        if (root is null || position == 0) return null;
        var token = root.FindToken(position - 1, findInsideTrivia: true);
        if (token.Parent?.AncestorsAndSelf().Any(static node => node is DocumentationCommentTriviaSyntax) == true
            || root.FindTrivia(position - 1).IsKind(SyntaxKind.SingleLineCommentTrivia)
            || root.FindTrivia(position - 1).IsKind(SyntaxKind.MultiLineCommentTrivia)) return null;
        var list = token.Parent?.AncestorsAndSelf().FirstOrDefault(node => node switch {
            ArgumentListSyntax arguments => arguments.OpenParenToken.Span.End <= position
                && (arguments.CloseParenToken.IsMissing || position <= arguments.CloseParenToken.SpanStart),
            BracketedArgumentListSyntax arguments => arguments.OpenBracketToken.Span.End <= position
                && (arguments.CloseBracketToken.IsMissing || position <= arguments.CloseBracketToken.SpanStart),
            AttributeArgumentListSyntax arguments => arguments.OpenParenToken.Span.End <= position
                && (arguments.CloseParenToken.IsMissing || position <= arguments.CloseParenToken.SpanStart),
            _ => false,
        });
        if (list is null) return null;
        var model = await document.GetSemanticModelAsync(cancellation);
        if (model is null) return null;
        var enclosing = model.GetEnclosingSymbol(list.SpanStart, cancellation);
        ISymbol within = enclosing as INamedTypeSymbol ?? (ISymbol?)enclosing?.ContainingType ?? model.Compilation.Assembly;
        var info = model.GetSymbolInfo(list.Parent!, cancellation);
        var symbols = new List<ISymbol>();
        if (info.Symbol is IMethodSymbol or IPropertySymbol) symbols.Add(info.Symbol);
        symbols.AddRange(info.CandidateSymbols);
        switch (list.Parent) {
            case InvocationExpressionSyntax invocation:
                symbols.AddRange(model.GetMemberGroup(invocation.Expression, cancellation));
                var type = model.GetTypeInfo(invocation.Expression, cancellation).Type;
                if (type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke }) symbols.Add(invoke);
                if (type is IFunctionPointerTypeSymbol pointer) symbols.Add(pointer.Signature);
                break;
            case BaseObjectCreationExpressionSyntax creation:
                if (model.GetTypeInfo(creation, cancellation).Type is INamedTypeSymbol created) symbols.AddRange(created.InstanceConstructors);
                break;
            case ConstructorInitializerSyntax initializer:
                var owner = enclosing?.ContainingType;
                if (initializer.IsKind(SyntaxKind.BaseConstructorInitializer)) owner = owner?.BaseType;
                if (owner is not null) symbols.AddRange(owner.InstanceConstructors);
                break;
            case AttributeSyntax attribute:
                if (model.GetTypeInfo(attribute, cancellation).Type is INamedTypeSymbol attrType) symbols.AddRange(attrType.InstanceConstructors);
                break;
            case ElementAccessExpressionSyntax access:
                for (var ownerType = model.GetTypeInfo(access.Expression, cancellation).Type as INamedTypeSymbol;
                    ownerType is not null; ownerType = ownerType.BaseType) {
                    symbols.AddRange(ownerType.GetMembers().OfType<IPropertySymbol>().Where(static property => property.IsIndexer));
                }
                break;
        }
        var arguments = list is BaseArgumentListSyntax ordinary
            ? ordinary.Arguments.Select(static argument => (argument.Expression, argument.NameColon?.Name.Identifier.ValueText,
                argument.RefKindKeyword.Kind())).ToArray()
            : ((AttributeArgumentListSyntax)list).Arguments.Select(static argument => (argument.Expression,
                argument.NameColon?.Name.Identifier.ValueText ?? argument.NameEquals?.Name.Identifier.ValueText, SyntaxKind.None)).ToArray();
        var separators = list is BaseArgumentListSyntax standard ? standard.Arguments.GetSeparators()
            : ((AttributeArgumentListSyntax)list).Arguments.GetSeparators();
        var ordinal = separators.Count(separator => separator.SpanStart < position);
        var activeName = ordinal < arguments.Length ? arguments[ordinal].Item2 : null;
        var ranked = new List<(SignatureItem Item, int Score, string Key)>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var symbol in symbols) {
            cancellation.ThrowIfCancellationRequested();
            if (!seen.Add(symbol.OriginalDefinition) || symbol is not (IMethodSymbol or IPropertySymbol)
                || !model.Compilation.IsSymbolAccessibleWithin(symbol, within)) continue;
            var parameters = symbol is IMethodSymbol method ? method.Parameters : ((IPropertySymbol)symbol).Parameters;
            var active = ParameterIndex(parameters, ordinal, activeName);
            var score = SymbolEqualityComparer.Default.Equals(symbol, info.Symbol) ? 10000 : 0;
            var applicable = active >= 0 || parameters.Length == 0 && arguments.Length == 0;
            var assigned = new HashSet<int>();
            for (var index = 0; index < arguments.Length; index++) {
                var (expression, name, modifier) = arguments[index];
                if (expression.IsMissing) continue;
                var mapped = ParameterIndex(parameters, index, name);
                if (mapped < 0 || !assigned.Add(mapped) && !parameters[mapped].IsParams) { applicable = false; continue; }
                var parameter = parameters[mapped];
                if ((modifier == SyntaxKind.RefKeyword) != (parameter.RefKind == RefKind.Ref)
                    || (modifier == SyntaxKind.OutKeyword) != (parameter.RefKind == RefKind.Out)) applicable = false;
                var conversion = model.ClassifyConversion(expression, parameter.Type);
                if (!conversion.IsImplicit && parameter.IsParams && parameter.Type is IArrayTypeSymbol array)
                    conversion = model.ClassifyConversion(expression, array.ElementType);
                if (conversion.IsIdentity) score += 20;
                else if (conversion.IsImplicit) score += 10;
                else if (model.GetTypeInfo(expression, cancellation).Type is not null && parameter.Type.TypeKind != TypeKind.TypeParameter)
                    applicable = false;
            }
            if (applicable) score += 1000;
            score -= Math.Abs(parameters.Length - Math.Max(arguments.Length, ordinal + 1));
            var (summary, parameterDocs) = Documentation(symbol, active >= 0 ? parameters[active].Name : null, cancellation);
            var indexer = symbol is IPropertySymbol;
            var item = new SignatureItem(symbol.ToMinimalDisplayString(model, position, _display) + (indexer ? "[" : "("),
                parameters.Select(parameter => parameter.ToMinimalDisplayString(model, position, _display)).ToArray(),
                indexer ? "]" : ")", active, summary, parameterDocs, applicable);
            ranked.Add((item, score, item.Prefix + string.Join(",", item.Parameters)));
        }
        if (ranked.Count == 0) return null;
        var items = ranked.OrderByDescending(static item => item.Score).ThenBy(static item => item.Key, StringComparer.Ordinal).ToArray();
        return new(list.SpanStart, items.Select(static item => item.Item).ToArray(), 0);
    }

    private static int ParameterIndex(ImmutableArray<IParameterSymbol> parameters, int ordinal, string? name)
    {
        if (name is not null) {
            for (var index = 0; index < parameters.Length; index++) if (parameters[index].Name == name) return index;
            return -1;
        }
        return ordinal < parameters.Length ? ordinal : parameters is [.., { IsParams: true }] ? parameters.Length - 1 : -1;
    }

    private static (string Summary, string Parameter) Documentation(ISymbol symbol, string? parameter, CancellationToken cancellation)
    {
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellation);
        if (string.IsNullOrWhiteSpace(xml)) return ("", "");
        try {
            var document = XElement.Parse(xml);
            static string Text(XElement? node) => string.Join(' ', (node?.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return (Text(document.Element("summary")), Text(document.Elements("param").FirstOrDefault(node => (string?)node.Attribute("name") == parameter)));
        }
        catch (System.Xml.XmlException) { return ("", ""); }
    }
}
