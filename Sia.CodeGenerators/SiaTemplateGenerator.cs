namespace Sia.CodeGenerators;

using System.Text;
using System.Diagnostics;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Common;

[Generator]
internal partial class SiaTemplateGenerator : IIncrementalGenerator
{
    private record CodeGenerationInfo(
        INamespaceSymbol Namespace,
        ImmutableArray<TypeDeclarationSyntax> ParentTypes,
        TypeDeclarationSyntax TemplateType, string? TypeConstraints,
        string ComponentName,
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

                static IEnumerable<(string, string)> GetProperties(INamedTypeSymbol symbol)
                {
                    var result = symbol.GetMembers().SelectMany(member => member switch {
                        IFieldSymbol fieldSymbol =>
                            IsValidTemplateMember(fieldSymbol)
                                ? ImmutableArray.Create((fieldSymbol.Name, fieldSymbol.Type.ToDisplayString()))
                                : Enumerable.Empty<(string, string)>(),
                        IPropertySymbol propSymbol =>
                            IsValidTemplateMember(propSymbol)
                                ? ImmutableArray.Create((propSymbol.Name, propSymbol.Type.ToDisplayString()))
                                : Enumerable.Empty<(string, string)>(),
                        _ => Enumerable.Empty<(string, string)>()
                    });
                    return symbol.BaseType != null ? result.Concat(GetProperties(symbol.BaseType)) : result;
                }

                var targetSymbol = model.GetDeclaredSymbol(targetType, token)!;
                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    ParentTypes: parentTypes,
                    TemplateType: targetType,
                    TypeConstraints: 
                        targetType.TypeParameterList != null
                            ? GetTypeConstraints(targetSymbol) : null,
                    ComponentName: syntax.Attributes[0].ConstructorArguments[0].Value as string
                        ?? throw new InvalidDataException("Invalid attribute"),
                    Properties: GetProperties(targetSymbol).ToImmutableArray()
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetTypeConstraints(INamedTypeSymbol targetSymbol)
    {
        var fullTemplateTypeString = 
            targetSymbol.ToDisplayString(
                SymbolDisplayFormats.QualifiedTypeNameWithTypeConstraints);
        return fullTemplateTypeString[(fullTemplateTypeString.IndexOf('>') + 2)..];
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
        builder.Append(info.ComponentName);
        builder.Append(".g.cs");
        return builder.ToString();
    }

    private static void GenerateSource(IndentedTextWriter source, CodeGenerationInfo info)
    {
        var templateType = info.TemplateType;
        var componentName = info.ComponentName;
        var properties = info.Properties;

        using (GenerateInNamespace(source, info.Namespace)) {
            using (GenerateInPartialTypes(source, info.ParentTypes)) {
                source.Write("public partial record struct ");
                source.Write(info.ComponentName);
                WriteTypeParameters(source, templateType);
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

                source.Write(") : global::Sia.IConstructable<");
                source.Write(templateType.Identifier.ToString());
                WriteTypeParameters(source, templateType);
                source.WriteLine(">");
                if (info.TypeConstraints != null) {
                    source.WriteLine(info.TypeConstraints);
                }
                source.Indent--;

                source.WriteLine("{");
                source.Indent++;

                GenerateConstructor(source,
                    templateType: templateType,
                    componentName: componentName,
                    properties: properties);
                source.WriteLine();

                GenerateConstructMethod(source,
                    templateType: templateType,
                    properties: properties);
                source.WriteLine();

                foreach (var (name, type) in properties) {
                    SiaPropertyGenerator.GenerateSetCommand(source,
                        commandName: "Set" + name,
                        componentName: componentName,
                        componentTypeParams: templateType.TypeParameterList,
                        valueName: name,
                        valueType: type);
                    source.WriteLine();
                }

                GenerateResetCommand(source,
                    templateType: templateType,
                    componentName: componentName,
                    properties: properties);

                source.Indent--;
                source.WriteLine("}");
            }
        }

        Debug.Assert(source.Indent == 0);
    }

    public static void GenerateConstructor(
        IndentedTextWriter source, TypeDeclarationSyntax templateType, string componentName, ImmutableArray<(string, string)> properties)
    {
        source.Write("public ");
        source.Write(componentName);
        source.Write("(");
        WriteType(source, templateType);
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

    public static void GenerateConstructMethod(
        IndentedTextWriter source, TypeDeclarationSyntax templateType, ImmutableArray<(string, string)> properties)
    {
        source.WriteLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        source.Write("public void Construct(");
        WriteType(source, templateType);
        source.WriteLine(" template)");
        source.WriteLine("{");
        source.Indent++;

        foreach (var (name, _) in properties) {
            source.Write(name);
            source.Write(" = template.");
            source.Write(name);
            source.WriteLine(";");
        }

        source.Indent--;
        source.WriteLine("}");
    }

    public static void GenerateResetCommand(
        IndentedTextWriter source, TypeDeclarationSyntax templateType, string componentName, ImmutableArray<(string, string)> properties)
    {
        source.Write("public readonly record struct Reset(");
        WriteType(source, templateType);
        source.WriteLine(" Value) : global::Sia.ICommand");
        source.WriteLine("{");
        source.Indent++;

        source.WriteLine("public void Execute(global::Sia.World _, in global::Sia.EntityRef target)");
        source.Indent++;
        source.Write("=> target.Get<");
        source.Write(componentName);
        WriteTypeParameters(source, templateType);
        source.WriteLine(">().Construct(Value);");

        source.Indent -= 2;
        source.WriteLine("}");
    }
}