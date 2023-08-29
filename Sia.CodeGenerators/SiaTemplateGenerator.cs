namespace Sia.CodeGenerators;

using System.Text;
using System.Diagnostics;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Common;
using System.Runtime.CompilerServices;

[Generator]
internal partial class SiaTemplateGenerator : IIncrementalGenerator
{
    private record CodeGenerationInfo(
        INamespaceSymbol Namespace,
        ImmutableArray<TypeDeclarationSyntax> ParentTypes,
        string TemplateName, string ComponentName,
        ImmutableArray<(string Name, string Type)> Properties);
    
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static context => {
            context.AddSource("SiaTemplateAttribute.g.cs",
                SourceText.From(SiaTemplateAttributeSource, Encoding.UTF8));
            context.AddSource("SiaIgnoreAttribute.g.cs",
                SourceText.From(SiaIgnoreAttributeSource, Encoding.UTF8));
        });

        var codeGenInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
            SiaTemplateAttributeName,
            static (syntaxNode, token) => true,
            static (syntax, token) =>
                (syntax, ParentTypes: GetParentTypes(syntax.TargetNode)))
            .Where(static t => t.ParentTypes.All(
                static typeDecl => typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword)))
            .Select(static (t, token) => {
                var (syntax, parentTypes) = t;
                var model = syntax.SemanticModel;
                var targetType = (TypeDeclarationSyntax)syntax.TargetNode;
                var typeSymbol = model.GetDeclaredSymbol(targetType, token)!;

                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    ParentTypes: parentTypes,
                    TemplateName: targetType.Identifier.ToString(),
                    ComponentName: syntax.Attributes[0].ConstructorArguments[0].Value as string
                        ?? throw new InvalidDataException("Invalid attribute"),
                    Properties:
                        typeSymbol.GetMembers().SelectMany(member => member switch {
                            IFieldSymbol fieldSymbol =>
                                IsValidTemplateMember(fieldSymbol)
                                    ? ImmutableArray.Create((fieldSymbol.Name, fieldSymbol.Type.ToDisplayString()))
                                    : Enumerable.Empty<(string, string)>(),
                            IPropertySymbol propSymbol =>
                                IsValidTemplateMember(propSymbol)
                                    ? ImmutableArray.Create((propSymbol.Name, propSymbol.Type.ToDisplayString()))
                                    : Enumerable.Empty<(string, string)>(),
                            _ => Enumerable.Empty<(string, string)>()
                        }).ToImmutableArray()
                );
            });
        
        context.RegisterSourceOutput(codeGenInfos , static (context, info) => {
            using var source = CreateSource(out var builder);
            GenerateSource(source, info);
            context.AddSource(GenerateFileName(info), builder.ToString());
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidTemplateMember(ISymbol symbol)
        => !symbol.IsStatic && symbol.DeclaredAccessibility == Accessibility.Public
            && !symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.ToDisplayString() == SiaIgnoreAttributeName);

    private static string GenerateFileName(CodeGenerationInfo info)
    {
        var builder = new StringBuilder();
        builder.Append(info.Namespace.ToDisplayString());
        builder.Append('.');
        foreach (var parentType in info.ParentTypes) {
            builder.Append(parentType.Identifier.ToString());
            builder.Append('.');
        }
        builder.Append(info.ComponentName);
        builder.Append(".g.cs");
        return builder.ToString();
    }

    private static void GenerateSource(IndentedTextWriter source, CodeGenerationInfo info)
    {
        var templateName = info.TemplateName;
        var componentName = info.ComponentName;
        var properties = info.Properties;

        using (GenerateInNamespace(source, info.Namespace)) {
            using (GenerateInPartialTypes(source, info.ParentTypes)) {
                source.Write("public partial record struct ");
                source.Write(componentName);
                source.WriteLine("(");
                source.Indent++;

                int index = 0;
                int lastIndex = properties.Length - 1;

                foreach (var (name, type) in properties) {
                    source.Write(type);
                    source.Write(" ");
                    source.Write(name);

                    if (index != lastIndex) {
                        source.WriteLine(", ");
                    }
                    index++;
                }

                source.WriteLine(")");
                source.Indent--;

                source.WriteLine("{");
                source.Indent++;

                GenerateConstructor(source,
                    templateName: templateName,
                    componentName: componentName,
                    properties: properties);
                source.WriteLine();

                foreach (var (name, type) in properties) {
                    SiaPropertyGenerator.GenerateSetCommand(source,
                        commandName: "Set" + name,
                        componentName: componentName,
                        valueName: name,
                        valueType: type);
                    source.WriteLine();
                }

                GenerateResetCommand(source,
                    templateName: templateName,
                    componentName: componentName,
                    properties: properties);

                source.Indent--;
                source.WriteLine("}");
            }
        }

        Debug.Assert(source.Indent == 0);
    }

    public static void GenerateConstructor(
        IndentedTextWriter source, string templateName, string componentName, ImmutableArray<(string, string)> properties)
    {
        source.Write("public ");
        source.Write(componentName);
        source.Write("(");
        source.Write(templateName);
        source.WriteLine(" template)");
        source.Indent++;
        source.WriteLine(": this(");
        source.Indent++;

        int index = 0;
        int lastIndex = properties.Length - 1;

        foreach (var (name, _) in properties) {
            source.Write(name);
            source.Write(": template.");
            source.Write(name);

            if (index != lastIndex) {
                source.WriteLine(",");
            }
            index++;
        }

        source.WriteLine(") {}");
        source.Indent -= 2;
    }

    public static void GenerateResetCommand(
        IndentedTextWriter source, string templateName, string componentName, ImmutableArray<(string, string)> properties)
    {
        source.Write("public readonly record struct Reset(");
        source.Write(templateName);
        source.WriteLine(" Value) : global::Sia.ICommand");
        source.WriteLine("{");
        source.Indent++;

        source.WriteLine("public void Execute(global::Sia.World<global::Sia.EntityRef> _, in global::Sia.EntityRef target)");
        source.WriteLine("{");
        source.Indent++;

        source.Write("ref var comp = ref target.Get<");
        source.Write(componentName);
        source.WriteLine(">();");

        foreach (var (name, _) in properties) {
            source.Write("comp.");
            source.Write(name);
            source.Write(" = Value.");
            source.Write(name);
            source.WriteLine(";");
        }

        source.Indent--;
        source.WriteLine("}");
        
        source.Indent--;
        source.WriteLine("}");
    }
}