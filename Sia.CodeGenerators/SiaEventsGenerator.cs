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
internal partial class SiaEventsGenerator : IIncrementalGenerator
{
    private record CodeGenerationInfo(
        INamespaceSymbol Namespace,
        TypeDeclarationSyntax ComponentType,
        ImmutableArray<TypeDeclarationSyntax> ParentTypes,
        ImmutableArray<INamedTypeSymbol> EventTypes);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static context => {
            context.AddSource("SiaEventsAttribute.g.cs",
                SourceText.From(SiaEventsAttributeSource, Encoding.UTF8));
        });

        var codeGenInfos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SiaEventsAttributeName,
                static (syntaxNode, _) =>
                    ((TypeDeclarationSyntax)syntaxNode).Modifiers.Any(SyntaxKind.PartialKeyword),
                static (syntax, _) =>
                    (syntax, parentTypes: GetParentTypes(syntax.TargetNode)))
            .Select(static (t, token) => {
                var (syntax, parentTypes) = t;
                var model = syntax.SemanticModel;
                var targetType = (TypeDeclarationSyntax)syntax.TargetNode;
                var targetSymbol = model.GetDeclaredSymbol(targetType, token)!;

                static IEnumerable<INamedTypeSymbol> GetEventTypes(INamedTypeSymbol symbol)
                    => symbol.GetTypeMembers().Where(IsValidEventType);

                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    ComponentType: targetType,
                    ParentTypes: parentTypes,
                    EventTypes: GetEventTypes(targetSymbol).ToImmutableArray());
            });

        context.RegisterSourceOutput(codeGenInfos, static (context, info) => {
            using var source = CreateFileSource(out var builder);

            using (GenerateInNamespace(source, info.Namespace)) {
                using (GenerateInPartialTypes(source, info.ParentTypes)) {
                    using (GenerateInComponentType(source, info.ComponentType)) {
                        GenerateEventTypesProperty(source, info.EventTypes);
                        source.WriteLine();
                        GenerateHandleEventTypesMethod(source, info.EventTypes);
                    }
                }
            }

            context.AddSource(GenerateFileName(info), builder.ToString());
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidEventType(INamedTypeSymbol symbol)
        => symbol.DeclaredAccessibility == Accessibility.Public
            && !symbol.IsAbstract
            && symbol.AllInterfaces.Any(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Sia.IEvent");

    private static string GenerateFileName(CodeGenerationInfo info)
    {
        var builder = new StringBuilder();
        builder.Append(info.Namespace.ToDisplayString());
        builder.Append('.');
        foreach (var parentType in info.ParentTypes) {
            builder.Append(parentType.Identifier.ToString());
            builder.Append('.');
        }
        builder.Append(info.ComponentType.Identifier.ToString());
        builder.Append('.');
        builder.Append("Events.g.cs");
        return builder.ToString();
    }

    private static EnclosingDisposable GenerateInComponentType(IndentedTextWriter source, TypeDeclarationSyntax componentType)
    {
        switch (componentType.Kind()) {
            case SyntaxKind.StructDeclaration:
                source.Write("partial struct ");
                break;
            case SyntaxKind.RecordStructDeclaration:
                source.Write("partial record struct ");
                break;
            case SyntaxKind.ClassDeclaration:
                source.Write("partial class ");
                break;
            case SyntaxKind.RecordDeclaration:
                source.Write("partial record ");
                break;
            default:
                throw new InvalidDataException("Invalid component type");
        }
        WriteType(source, componentType);
        source.WriteLine(" : global::Sia.IEventUnion");
        source.WriteLine("{");
        source.Indent++;
        return new EnclosingDisposable(source, 1);
    }

    public static void GenerateEventTypesProperty(
        IndentedTextWriter source, ImmutableArray<INamedTypeSymbol> eventTypes)
    {
        source.WriteLine("public static global::Sia.ITypeUnion StaticEventTypes { get; }");
        source.Indent++;
        source.Write("= new global::Sia.TypeUnion(");

        var lastIndex = eventTypes.Length - 1;
        for (int i = 0; i != lastIndex; ++i) {
            source.Write("typeof(");
            source.Write(eventTypes[i].Name);
            source.Write("), ");
        }

        source.Write("typeof(");
        source.Write(eventTypes[lastIndex].Name);
        source.WriteLine("));");
        source.Indent--;

        source.WriteLine("public global::Sia.ITypeUnion EventTypes => StaticEventTypes;");
    }

    public static void GenerateHandleEventTypesMethod(
        IndentedTextWriter source, ImmutableArray<INamedTypeSymbol> eventTypes)
    {
        source.WriteLine("public static void HandleEventTypes(global::Sia.IGenericTypeHandler<global::Sia.IEvent> handler)");
        source.WriteLine("{");
        source.Indent++;

        foreach (var eventType in eventTypes) {
            source.Write("handler.Handle<");
            source.Write(eventType.Name);
            source.WriteLine(">();");
        }

        source.Indent--;
        source.WriteLine("}");
    }
}