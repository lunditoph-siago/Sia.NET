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
        ImmutableArray<PropertyInfo> Properties,
        bool Immutable);
    
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

                static IEnumerable<PropertyInfo> GetProperties(INamedTypeSymbol symbol)
                {
                    var result = symbol.GetMembers().SelectMany(member => member switch {
                        IFieldSymbol fieldSymbol =>
                            IsValidTemplateMember(fieldSymbol)
                                ? ImmutableArray.Create(
                                    new PropertyInfo(fieldSymbol.Name, fieldSymbol.Type, symbol, member.GetAttributes()))
                                : Enumerable.Empty<PropertyInfo>(),
                        IPropertySymbol propSymbol =>
                            IsValidTemplateMember(propSymbol)
                                ? ImmutableArray.Create(
                                    new PropertyInfo(propSymbol.Name, propSymbol.Type, symbol, member.GetAttributes()))
                                : Enumerable.Empty<PropertyInfo>(),
                        _ => []
                    });
                    return symbol.BaseType != null ? result.Concat(GetProperties(symbol.BaseType)) : result;
                }

                var targetSymbol = model.GetDeclaredSymbol(targetType, token)!;
                var templateAttr = syntax.Attributes[0];

                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    ParentTypes: parentTypes,
                    TemplateType: targetType,
                    TypeConstraints: 
                        targetType.TypeParameterList != null
                            ? GetTypeConstraints(targetSymbol) : null,
                    ComponentName: templateAttr.ConstructorArguments[0].Value as string
                        ?? throw new InvalidDataException("Invalid attribute"),
                    Properties: GetProperties(targetSymbol).ToImmutableArray(),
                    Immutable: templateAttr.NamedArguments.Any(
                        p => p.Key == "Immutable" && (bool)p.Value.Value! == true)
                );
            });
        
        context.RegisterSourceOutput(codeGenInfos , static (context, info) => {
            using var source = CreateFileSource(out var builder);

            using (GenerateInNamespace(source, info.Namespace)) {
                using (GenerateInPartialTypes(source, info.ParentTypes)) {
                    using (GenerateInComponentType(source, info)) {
                        var templateType = info.TemplateType;
                        var compName = info.ComponentName;
                        var properties = info.Properties;
                        var immutable = info.Immutable;

                        GenerateConstructor(source, templateType, compName, properties);

                        source.WriteLine();
                        GenerateConstructMethod(source, templateType, compName, properties);

                        if (!immutable) {
                            source.WriteLine();

                            GenerateResetCommand(source,
                                templateType: templateType,
                                componentName: compName,
                                properties: properties);
                            
                            if (properties.Length != 0) {
                                source.WriteLine();
                                SiaPropertyGenerator.GenerateViewTypeMainDecl(source, compName, templateType.TypeParameterList);

                                foreach (var propInfo in properties) {
                                    source.WriteLine();
                                    SiaPropertyGenerator.GeneratePropertyCommands(
                                        source, propInfo, compName, templateType.TypeParameterList);

                                    source.WriteLine();
                                    SiaPropertyGenerator.GenerateViewTypeProperty(source, propInfo);
                                }
                            }
                        }
                    }
                }
            }

            Debug.Assert(source.Indent == 0);
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

    private static IDisposable GenerateInComponentType(
        IndentedTextWriter source, CodeGenerationInfo info)
    {
        var templateType = info.TemplateType;
        var properties = info.Properties;
        var immutable = info.Immutable;

        source.Write(immutable
            ? "public readonly partial record struct "
            : "public partial record struct ");
        source.Write(info.ComponentName);
        WriteTypeParameters(source, templateType);
        source.WriteLine("(");
        source.Indent++;

        int index = 0;
        int lastIndex = properties.Length - 1;

        foreach (var prop in properties) {
            source.Write(prop.DisplayType);
            source.Write(" ");
            source.Write(prop.Name);

            if (index != lastIndex) {
                source.WriteLine(", ");
            }
            index++;
        }

        source.Write(") : global::Sia.IConstructable<");
        source.Write(info.ComponentName);
        WriteTypeParameters(source, templateType);
        source.Write(", ");
        source.Write(templateType.Identifier.ToString());
        WriteTypeParameters(source, templateType);
        source.WriteLine(">");
        if (info.TypeConstraints != null) {
            source.WriteLine(info.TypeConstraints);
        }
        source.Indent--;

        source.WriteLine("{");
        source.Indent++;

        return new EnclosingDisposable(source, 1);
    }

    public static void GenerateConstructor(
        IndentedTextWriter source, TypeDeclarationSyntax templateType, string componentName, ImmutableArray<PropertyInfo> properties)
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

        foreach (var prop in properties) {
            source.Write(prop.Name);
            source.Write(": template.");
            source.Write(prop.Name);

            if (index != lastIndex) {
                source.WriteLine(",");
            }
            index++;
        }

        source.WriteLine(") {}");
        source.Indent -= 2;
    }

    public static void GenerateConstructMethod(
        IndentedTextWriter source, TypeDeclarationSyntax templateType, string compName, ImmutableArray<PropertyInfo> properties)
    {
        source.Write("public static void Construct(");
        WriteType(source, templateType);
        source.Write(" template, out ");
        source.Write(compName);
        WriteTypeParameters(source, templateType);
        source.WriteLine(" component)");
        source.Indent++;
        source.WriteLine("=> component = new(template);");
        source.Indent--;
    }

    public static void GenerateResetCommand(
        IndentedTextWriter source, TypeDeclarationSyntax templateType, string componentName, ImmutableArray<PropertyInfo> properties)
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
        source.WriteLine(">() = new(Value);");

        source.Indent -= 2;
        source.WriteLine("}");
    }
}