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
        INamedTypeSymbol NamedTypeSymbol,
        SemanticModel SemanticModel,
        ImmutableArray<TypeDeclarationSyntax> ParentTypes);

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
                    syntaxNode is StructDeclarationSyntax structDeclarationSyntax
                    && structDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword),
                static (syntax, _) =>
                    (syntax, parentTypes: GetParentTypes(syntax.TargetNode)))
            .Select(static (t, token) => {
                var (syntax, parentTypes) = t;

                var structDeclaration = syntax.TargetNode;
                var model = syntax.SemanticModel;

                return structDeclaration is StructDeclarationSyntax &&
                       model.GetDeclaredSymbol(structDeclaration, token) is INamedTypeSymbol structSymbol
                    ? new CodeGenerationInfo(
                        Namespace: syntax.TargetSymbol.ContainingNamespace,
                        NamedTypeSymbol: structSymbol,
                        SemanticModel: model,
                        ParentTypes: parentTypes
                    )
                    : null;
            });

        context.RegisterSourceOutput(codeGenInfos, static (context, info) =>
        {
            if (info is null) return;

            using var source = CreateFileSource(out var builder);

            using (GenerateInNamespace(source, info.Namespace)) {
                var members = GetMembersWithDefaults(info.NamedTypeSymbol, info.SemanticModel);

                if (!members.Any()) return;

                source.WriteLine($"public partial struct {info.NamedTypeSymbol.Name} : global::Sia.IBundle");
                source.WriteLine("{");
                source.Indent++;

                source.WriteLine($"public static {GenerateType(members)} BakedEntity => {GenerateNestedHLists(members)};");
                source.WriteLine("");
                source.WriteLine("public readonly void ToMany(global::Sia.IGenericHandler<global::Sia.IHList> handler)");
                source.WriteLine("{");
                source.Indent++;
                source.WriteLine($"handler.Handle({GenerateNestedHLists(members)});");
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

    private static List<(ISymbol member, object? defaultValue)> GetMembersWithDefaults(INamedTypeSymbol structSymbol, SemanticModel semanticModel)
    {
        var membersWithDefaults = new List<(ISymbol, object?)>();

        foreach (var member in structSymbol.GetMembers()) {
            object? defaultValue = null;

            switch (member) {
                case IFieldSymbol { DeclaredAccessibility: Accessibility.Public, HasConstantValue: true } field:
                    defaultValue = field.ConstantValue;
                    break;
                case IPropertySymbol { DeclaredAccessibility: Accessibility.Public } property:
                    var propertySyntax = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;
                    if (propertySyntax?.Initializer is not null) {
                        var constantValue = semanticModel.GetConstantValue(propertySyntax.Initializer.Value);
                        if (constantValue.HasValue) defaultValue = constantValue.Value;
                    }
                    break;
            }

            if (member is IFieldSymbol or IPropertySymbol) {
                membersWithDefaults.Add((member, defaultValue));
            }
        }

        foreach (var parameter in structSymbol.Constructors.FirstOrDefault()?.Parameters ??
                                  ImmutableArray<IParameterSymbol>.Empty) {
            if (parameter.HasExplicitDefaultValue) {
                membersWithDefaults.Add((parameter, parameter.ExplicitDefaultValue));
            }
        }

        return membersWithDefaults;
    }

    private static string GenerateType(List<(ISymbol member, object? defaultValue)> members)
    {
        var nestedType = new StringBuilder();

        foreach (var member in members) {
            var type = member.member switch {
                IParameterSymbol parameterSymbol => parameterSymbol.Type.ToDisplayString(),
                IFieldSymbol fieldSymbol => fieldSymbol.Type.ToDisplayString(),
                IPropertySymbol propertySymbol => propertySymbol.Type.ToDisplayString(),
                _ => throw new ArgumentException("Unsupported symbol type", nameof(member))
            };

            nestedType.Append($"global::Sia.HList<{type}, ");
        }

        nestedType.Append("global::Sia.EmptyHList" + new string('>', members.Count));

        return nestedType.ToString();
    }

    private static string GenerateNestedHLists(List<(ISymbol member, object? defaultValue)> members)
    {
        var nestedHList = new StringBuilder();

        foreach (var member in members) {
            var (type, defaultValue) = member.member switch {
                IParameterSymbol param =>
                    (param.Type, param.Type.IsValueType ? $"default({param.Type})" : "null"),
                IFieldSymbol field =>
                    (field.Type, field.Type.IsValueType ? $"default({field.Type})" : "null"),
                IPropertySymbol property =>
                    (property.Type, property.Type.IsValueType ? $"default({property.Type})" : "null"),
                _ => throw new ArgumentException("Unsupported symbol type", nameof(member))
            };

            nestedHList.Append($"global::Sia.HList.Cons(({type}){member.defaultValue ?? defaultValue}, ");
        }

        nestedHList.Append("global::Sia.EmptyHList.Default" + new string(')', members.Count));

        return nestedHList.ToString();
    }
}