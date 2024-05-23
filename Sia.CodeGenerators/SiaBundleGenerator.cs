namespace Sia.CodeGenerators;

using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Common;

[Generator]
internal partial class SiaBundleGenerator : IIncrementalGenerator
{
    private record ComponentInfo(string Name, ITypeSymbol Type)
    {
        public string DisplayType { get; } = GetDisplayType(Type);
    }

    private record CodeGenerationInfo(
        INamespaceSymbol Namespace,
        TypeDeclarationSyntax BundleType,
        ImmutableArray<TypeDeclarationSyntax> ParentTypes,
        ImmutableArray<ComponentInfo> Components,
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
                    ((TypeDeclarationSyntax)syntaxNode).Modifiers.Any(SyntaxKind.PartialKeyword),
                static (syntax, _) =>
                    (syntax, parentTypes: GetParentTypes(syntax.TargetNode)))
            .Select(static (t, token) => {
                var (syntax, parentTypes) = t;
                var model = syntax.SemanticModel;
                var targetType = (TypeDeclarationSyntax)syntax.TargetNode;
                var targetSymbol = model.GetDeclaredSymbol(targetType, token)!;

                static IEnumerable<ComponentInfo> GetComponents(INamedTypeSymbol symbol)
                    => symbol.GetMembers()
                        .Where(IsValidBundleElement)
                        .Select<ISymbol, ComponentInfo?>(member => member switch {
                            IFieldSymbol field => new(field.Name, field.Type),
                            IPropertySymbol prop => new(prop.Name, prop.Type),
                            _ => null
                        })
                        .Where(c => c != null)
                        .Concat(symbol.BaseType != null ? GetComponents(symbol.BaseType) : [])!;

                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    BundleType: targetType,
                    ParentTypes: parentTypes,
                    Components: GetComponents(targetSymbol).ToImmutableArray(),
                    NamedTypeSymbol: targetSymbol);
            });

        context.RegisterSourceOutput(codeGenInfos, static (context, info) => {
            using var source = CreateFileSource(out var builder);

            using (GenerateInNamespace(source, info.Namespace)) {
                using (GenerateInPartialTypes(source, info.ParentTypes)) {
                    using (GenerateInBundleType(source, info.BundleType)) {
                        GenerateBakedProperty(source, info);
                        source.WriteLine();
                        GenerateToHListMethod(source, info);
                        source.WriteLine();
                        GenerateHandleHListTypeMethod(source, info);
                    }
                }
            }

            context.AddSource(GenerateFileName(info), builder.ToString());
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidBundleElement(ISymbol symbol)
        => !symbol.IsStatic && symbol.DeclaredAccessibility == Accessibility.Public;

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
        builder.Append("Bundle.g.cs");
        return builder.ToString();
    }

    private static EnclosingDisposable GenerateInBundleType(IndentedTextWriter source, TypeDeclarationSyntax bundleType)
    {
        switch (bundleType.Kind()) {
            case SyntaxKind.StructDeclaration:
                source.Write("partial struct ");
                break;
            case SyntaxKind.RecordStructDeclaration:
                source.Write("partial record struct ");
                break;
            default:
                throw new InvalidDataException("Invalid bundle type");
        }
        WriteType(source, bundleType);
        source.WriteLine(" : global::Sia.IBundle");
        source.WriteLine("{");
        source.Indent++;
        return new EnclosingDisposable(source, 1);
    }

    private static void GenerateBakedProperty(IndentedTextWriter source, CodeGenerationInfo info)
    {
        source.Write("public readonly ");
        GenerateHListType(source, info);
        source.Write(" Baked => ");
        GenerateHListValue(source, info);
        source.WriteLine(';');
    }

    private static void GenerateToHListMethod(IndentedTextWriter source, CodeGenerationInfo info)
    {
        source.WriteLine("public void ToHList<THandler>(in THandler handler)");
        source.Indent++;
        source.WriteLine("where THandler : global::Sia.IGenericHandler<global::Sia.IHList>");
        source.WriteLine("=> handler.Handle(Baked);");
        source.Indent--;
    }

    private static void GenerateHandleHListTypeMethod(IndentedTextWriter source, CodeGenerationInfo info)
    {
        source.WriteLine("public void HandleHListType<THandler>(in THandler handler)");
        source.Indent++;
        source.WriteLine("where THandler : global::Sia.IGenericTypeHandler<global::Sia.IHList>");
        source.WriteLine("=>");

        source.Write("handler.Handle<");
        GenerateHListType(source, info);
        source.WriteLine(">();");
        source.Indent--;
    }

    private static void GenerateHListType(IndentedTextWriter source, CodeGenerationInfo info)
    {
        foreach (var component in info.Components) {
            source.Write("global::Sia.HList<");
            source.Write(component.DisplayType);
            source.Write(", ");
        }
        source.Write("global::Sia.EmptyHList");
        source.Write(new string('>', info.Components.Length));
    }

    private static void GenerateHListValue(IndentedTextWriter source, CodeGenerationInfo info)
    {
        foreach (var component in info.Components) {
            source.Write("new(");
            source.Write(component.Name);
            source.Write(", ");
        }
        source.Write("global::Sia.EmptyHList.Default");
        source.Write(new string(')', info.Components.Length));
    }
}