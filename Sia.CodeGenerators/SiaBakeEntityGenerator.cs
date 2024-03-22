namespace Sia.CodeGenerators;

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Common;

[Generator]
internal partial class SiaBakeEntityGenerator : IIncrementalGenerator
{
    private record CodeGenerationInfo(
        INamespaceSymbol Namespace,
        TypeDeclarationSyntax ComponentType,
        ImmutableArray<TypeDeclarationSyntax> ParentTypes,
        ImmutableArray<(PropertyInfo, object?)> Properties,
        INamedTypeSymbol NamedTypeSymbol);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static context => {
            context.AddSource("SiaBakeEntityAttribute.g.cs",
                SourceText.From(SiaBakeEntityAttributeSource, Encoding.UTF8));
        });

        var codeGenInfos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SiaBakeEntityAttributeName,
                static (syntaxNode, _) =>
                    (syntaxNode is StructDeclarationSyntax structDeclarationSyntax && structDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword)) ||
                    (syntaxNode is RecordDeclarationSyntax recordDeclarationSyntax && recordDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword)),
                static (syntax, _) =>
                    (syntax, parentTypes: GetParentTypes(syntax.TargetNode)))
            .Select(static (t, token) => {
                var (syntax, parentTypes) = t;
                var model = syntax.SemanticModel;
                var targetType = (TypeDeclarationSyntax)syntax.TargetNode;

                var semanticModel = syntax.SemanticModel;
                var targetSymbol = model.GetDeclaredSymbol(targetType, token)!;

                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    ComponentType: targetType,
                    ParentTypes: parentTypes,
                    Properties: GetProperties(targetSymbol, semanticModel).ToImmutableArray(),
                    NamedTypeSymbol: targetSymbol);

                static IEnumerable<(PropertyInfo, object?)> GetProperties(INamedTypeSymbol symbol, SemanticModel model)
                {
                    var members = new List<(PropertyInfo, object?)>();

                    foreach (var member in symbol.GetMembers()) {
                        switch (member) {
                            case IFieldSymbol field:
                                members.Add(
                                    (new PropertyInfo(field.Name, field.Type, symbol, member.GetAttributes()),
                                        field.HasConstantValue ? field.ConstantValue : null));
                                break;
                            case IPropertySymbol prop:
                                var propertySyntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;
                                if (propertySyntax?.Initializer is not null) {
                                    var constantValue = model.GetConstantValue(propertySyntax.Initializer.Value);
                                    members.Add(
                                        (new PropertyInfo(prop.Name, prop.Type, symbol, member.GetAttributes()),
                                            constantValue.HasValue ? constantValue.Value : null));
                                }
                                break;
                        }
                    }

                    members.AddRange(
                        from parameter in symbol.Constructors.FirstOrDefault()?.Parameters
                                          ?? ImmutableArray<IParameterSymbol>.Empty
                        where parameter.HasExplicitDefaultValue
                        select
                            (new PropertyInfo(parameter.Name, parameter.Type, symbol, parameter.GetAttributes()),
                                parameter.ExplicitDefaultValue));

                    return symbol.BaseType is not null ? members.Concat(GetProperties(symbol.BaseType, model)) : members;
                }
            });

        context.RegisterSourceOutput(codeGenInfos, static (context, info) => {
            if (info is null) return;

            using var source = CreateFileSource(out var builder);

            using (GenerateInNamespace(source, info.Namespace)) {
                if (!info.Properties.Any()) return;

                source.WriteLine($"public partial {(info.ComponentType.Modifiers.Count > 0 ? $"{info.ComponentType.Keyword.Text} " : string.Empty)}struct {info.NamedTypeSymbol.Name} : global::Sia.IBundle");
                source.WriteLine("{");
                source.Indent++;

                source.WriteLine($"public static {GenerateType(info.Properties)} BakedEntity => {GenerateNestedHLists(info.Properties)};");
                source.WriteLine("");
                source.WriteLine("public readonly void ToHList(global::Sia.IGenericHandler<global::Sia.IHList> handler)");
                source.WriteLine("{");
                source.Indent++;

                source.WriteLine($"handler.Handle({GenerateNestedHLists(info.Properties)});");
                source.Indent--;
                source.WriteLine("}");

                source.Indent--;
                source.WriteLine("}");
            }

            context.AddSource(GenerateFileName(info), builder.ToString());
        });
    }

    private static string GenerateFileName(CodeGenerationInfo info)
    {
        var builder = new StringBuilder();
        builder.Append(info.Namespace.ToDisplayString());
        builder.Append('.');
        foreach (var parentType in info.ParentTypes) {
            builder.Append(parentType.Identifier.ToString());
            builder.Append('.');
        }
        builder.Append(info.NamedTypeSymbol.Name);
        builder.Append('.');
        builder.Append("BakedEntity.g.cs");
        return builder.ToString();
    }

    private static string GenerateType(ImmutableArray<(PropertyInfo property, object? defaultValue)> properties)
    {
        var nestedType = new StringBuilder();

        foreach (var property in properties) {
            nestedType.Append($"global::Sia.HList<{property.property.DisplayType}, ");
        }

        nestedType.Append("global::Sia.EmptyHList" + new string('>', properties.Length));

        return nestedType.ToString();
    }

    private static string GenerateNestedHLists(ImmutableArray<(PropertyInfo property, object? defaultValue)> properties)
    {
        var nestedHList = new StringBuilder();

        foreach (var property in properties) {
            var defaultValue = property.property.Type.IsValueType ? $"default({property.property.DisplayType})" : "null";
            nestedHList.Append($"global::Sia.HList.Cons(({property.property.DisplayType}){property.defaultValue ?? defaultValue}, ");
        }

        nestedHList.Append("global::Sia.EmptyHList.Default" + new string(')', properties.Length));

        return nestedHList.ToString();
    }
}