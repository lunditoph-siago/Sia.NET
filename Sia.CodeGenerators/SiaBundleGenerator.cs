using System.CodeDom.Compiler;

namespace Sia.CodeGenerators;

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Common;

[Generator]
internal partial class SiaBundleGenerator : IIncrementalGenerator
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
            context.AddSource("SiaBundleAttribute.g.cs",
                SourceText.From(SiaBundleAttributeSource, Encoding.UTF8));
        });

        var codeGenInfos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SiaBundleAttributeName,
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
                    var members = new Dictionary<string, (PropertyInfo, object?)>();

                    foreach (var member in symbol.GetMembers()) {
                        switch (member) {
                            case IFieldSymbol field: {
                                if (member is { IsStatic: false, DeclaredAccessibility: Accessibility.Public }) {
                                    members[member.Name] =
                                        (new PropertyInfo(member.Name, field.Type, symbol, member.GetAttributes()),
                                            field.HasConstantValue ? field.ConstantValue : null);
                                }
                                break;
                            }
                            case IPropertySymbol prop: {
                                if (member is { IsStatic: false, DeclaredAccessibility: Accessibility.Public }) {
                                    var constantValue =
                                        prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is
                                        PropertyDeclarationSyntax { Initializer: not null } propertySyntax
                                            ? model.GetConstantValue(propertySyntax.Initializer.Value).Value : null;
                                    members[member.Name] =
                                        (new PropertyInfo(member.Name, prop.Type, symbol, member.GetAttributes()),
                                            constantValue);
                                }
                                break;
                            }
                        }
                    }

                    foreach (var param in symbol.Constructors.FirstOrDefault()?.Parameters ??
                                          ImmutableArray<IParameterSymbol>.Empty)
                    {
                        members[param.Name] =
                            (new PropertyInfo(param.Name, param.Type, symbol, param.GetAttributes()),
                                param.HasExplicitDefaultValue ? param.ExplicitDefaultValue : null);
                    }

                    return symbol.BaseType is not null ? members.Values.Concat(GetProperties(symbol.BaseType, model)) : members.Values;
                }
            });

        context.RegisterSourceOutput(codeGenInfos, static (context, info) => {
            using var source = CreateFileSource(out var builder);

            using (GenerateInNamespace(source, info.Namespace)) {
                if (!info.Properties.Any()) return;

                source.WriteLine($"public partial {info.ComponentType.Kind() switch {
                    SyntaxKind.StructKeyword or SyntaxKind.StructDeclaration => "struct ",
                    SyntaxKind.RecordStructDeclaration => "record struct ",
                    _ => string.Empty
                }}{info.NamedTypeSymbol.Name} : global::Sia.IBundle");
                source.WriteLine("{");
                source.Indent++;

                source.WriteLine($"public static {GenerateNestedHListsType(info.Properties)} DefaultEntity => {GenerateNestedHLists(info.Properties, false)};");
                source.WriteLine($"public readonly {GenerateNestedHListsType(info.Properties)} BakedEntity => {GenerateNestedHLists(info.Properties)};");

                source.WriteLine();

                GenerateHandleMethod(source, "Head", "global::Sia.IGenericHandler");

                source.WriteLine();

                GenerateHandleMethod(source, "Tail", "global::Sia.IGenericHandler<global::Sia.IHList>");

                source.WriteLine();

                GenerateConcatMethod(source);

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

    private static void GenerateHandleMethod(IndentedTextWriter source, string position, string handle)
    {
        source.WriteLine($"public readonly void Handle{position}<THandler>(in THandler handler)");
        source.Indent++;
        source.WriteLine($"where THandler : {handle}");
        source.Indent--;
        source.WriteLine("{");
        source.Indent++;

        source.WriteLine($"handler.Handle(BakedEntity.{position});");
        source.Indent--;
        source.WriteLine("}");
    }

    private static void GenerateConcatMethod(IndentedTextWriter source)
    {
        source.WriteLine("public readonly void Concat<THList, TResultHandler>(in THList list, TResultHandler handler)");
        source.Indent++;
        source.WriteLine("where THList : global::Sia.IHList");
        source.WriteLine("where TResultHandler : global::Sia.IGenericHandler<global::Sia.IHList>");
        source.Indent--;
        source.WriteLine("{");
        source.Indent++;

        source.WriteLine($"BakedEntity.Concat(list, handler);");
        source.Indent--;
        source.WriteLine("}");
    }

    private static string GenerateNestedHListsType(ImmutableArray<(PropertyInfo property, object? defaultValue)> properties)
    {
        var nestedType = new StringBuilder();

        foreach (var property in properties) {
            nestedType.Append($"global::Sia.HList<{property.property.DisplayType}, ");
        }

        nestedType.Append("global::Sia.EmptyHList" + new string('>', properties.Length));

        return nestedType.ToString();
    }

    private static string GenerateNestedHLists(ImmutableArray<(PropertyInfo property, object? defaultValue)> properties, bool useField = true)
    {
        var nestedHList = new StringBuilder();

        foreach (var property in properties) {
            var defaultValue = property.property.Type.IsValueType ? $"default({property.property.DisplayType})" : "null";
            nestedHList.Append($"new(({property.property.DisplayType}){(useField ? property.property.Name : property.defaultValue?.ToString().ToLower() ?? defaultValue)}, ");
        }

        nestedHList.Append("global::Sia.EmptyHList.Default" + new string(')', properties.Length));

        return nestedHList.ToString();
    }
}