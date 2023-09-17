namespace Sia.CodeGenerators;

using System.Text;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Common;

[Generator]
internal partial class SiaPropertyGenerator : IIncrementalGenerator
{
    private record CodeGenerationInfo(
        INamespaceSymbol Namespace,
        TypeDeclarationSyntax ComponentType,
        ImmutableArray<TypeDeclarationSyntax> ParentTypes,
        ImmutableDictionary<string, TypedConstant> Arguments,
        string ValueName,
        string ValueType);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static context => {
            context.AddSource("SiaPropertyAttribute.g.cs",
                SourceText.From(SiaPropertyAttributeSource, Encoding.UTF8));
        });

        var codeGenInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
            SiaPropertyAttributeName,
            static (syntaxNode, token) =>
                FindParentNode<TypeDeclarationSyntax>(syntaxNode, out var parent)
                    && (parent.IsKind(SyntaxKind.StructDeclaration) || parent.IsKind(SyntaxKind.RecordStructDeclaration))
                    && parent.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && CheckWritable(syntaxNode),
            static (syntax, token) => {
                FindParentNode<TypeDeclarationSyntax>(syntax.TargetNode, out var componentType);
                return (syntax, componentType!, ParentTypes: GetParentTypes(componentType!));
            })
            .Where(static t => t.ParentTypes.All(
                static typeDecl => typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword)))
            .Select(static (t, token) => {
                var (syntax, containtingType, parentTypes) = t;
                var arguments = syntax.Attributes[0].NamedArguments.ToImmutableDictionary();

                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    ComponentType: containtingType,
                    ParentTypes: parentTypes,
                    Arguments: arguments,
                    ValueName: syntax.TargetSymbol.Name,
                    ValueType: syntax.TargetNode switch {
                        PropertyDeclarationSyntax propSyntax =>
                            GetFullType(syntax.SemanticModel, propSyntax.Type, token),
                        VariableDeclaratorSyntax varSyntax =>
                            GetVariableType(syntax.SemanticModel, varSyntax, token),
                        ParameterSyntax paramSyntax =>
                            GetFullType(syntax.SemanticModel, paramSyntax.Type!, token),
                        _ => throw new InvalidDataException("Invalid syntax")
                    }
                );
            });
        
        context.RegisterSourceOutput(codeGenInfos, static (context, info) => {
            using var source = CreateSource(out var builder);
            GenerateSource(info, source);
            context.AddSource(GenerateFileName(info), builder.ToString());
        });
    }

    private static bool CheckWritable(SyntaxNode node)
        => node switch {
            PropertyDeclarationSyntax propSyntax =>
                !propSyntax.Modifiers.Any(SyntaxKind.StaticKeyword)
                    && propSyntax.AccessorList!.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)),
            VariableDeclaratorSyntax varSyntax =>
                FindParentNode<FieldDeclarationSyntax>(varSyntax, out var fieldDecl)
                    && !fieldDecl.Modifiers.Any(m =>
                        m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.ReadOnlyKeyword)),
            ParameterSyntax paramSyntax =>
                paramSyntax.Parent!.Parent is RecordDeclarationSyntax recordDecl
                    && !recordDecl.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
            _ => false
        };

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
        builder.Append(info.ValueName);
        builder.Append(".g.cs");
        return builder.ToString();
    }

    private static void GenerateSource(CodeGenerationInfo info, IndentedTextWriter source)
    {
        var commandName = info.Arguments.TryGetValue("SetCommand", out var setCmdName)
            ? setCmdName.Value!.ToString()! : $"Set{info.ValueName}";

        using (GenerateInNamespace(source, info.Namespace)) {
            using (GenerateInPartialTypes(source, info.ParentTypes.Append(info.ComponentType))) {
                GenerateSetCommand(source,
                    commandName: commandName,
                    componentName: info.ComponentType.Identifier.ToString(),
                    valueName: info.ValueName,
                    valueType: info.ValueType);
            }
        }

        Debug.Assert(source.Indent == 0);
    }

    public static void GenerateSetCommand(
        IndentedTextWriter source, string commandName, string componentName, string valueName, string valueType)
    {
        source.Write("public readonly record struct ");
        source.Write(commandName);
        source.Write("(");
        source.Write(valueType);
        source.WriteLine(" Value) : global::Sia.ICommand");
        source.WriteLine("{");
        source.Indent++;

        source.WriteLine("public void Execute(global::Sia.World _, in global::Sia.EntityRef target)");
        source.Indent++;
        source.Write("=> target.Get<");
        source.Write(componentName);
        source.Write(">().");
        source.Write(valueName);
        source.WriteLine(" = Value;");
        source.Indent--;

        source.Indent--;
        source.WriteLine("}");

    }
}