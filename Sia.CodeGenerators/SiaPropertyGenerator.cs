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
        PropertyInfo Property)
    {
    }

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

                return new CodeGenerationInfo(
                    Namespace: syntax.TargetSymbol.ContainingNamespace,
                    ComponentType: containtingType,
                    ParentTypes: parentTypes,
                    Property: new(syntax.TargetSymbol.Name,
                        syntax.TargetNode switch {
                            PropertyDeclarationSyntax propSyntax =>
                                GetNodeType(syntax.SemanticModel, propSyntax.Type, token),
                            VariableDeclaratorSyntax varSyntax =>
                                GetVariableType(syntax.SemanticModel, varSyntax, token),
                            ParameterSyntax paramSyntax =>
                                GetNodeType(syntax.SemanticModel, paramSyntax.Type!, token),
                            _ => throw new InvalidDataException("Invalid syntax")
                        }, syntax.Attributes)
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
        builder.Append(info.Property.Name);
        builder.Append(".g.cs");
        return builder.ToString();
    }

    private static void GenerateSource(CodeGenerationInfo info, IndentedTextWriter source)
    {
        var componentType = info.ComponentType;
        using (GenerateInNamespace(source, info.Namespace)) {
            using (GenerateInPartialTypes(source, info.ParentTypes.Append(componentType))) {
                var compTypeStr = componentType.Identifier.ToString();
                var compTypeParams = componentType.TypeParameterList;
                GeneratePropertyCommands(source, info.Property, compTypeStr, compTypeParams);
            }
        }
        Debug.Assert(source.Indent == 0);
    }

    public static void GeneratePropertyCommands(
        IndentedTextWriter source, in PropertyInfo property,
        string componentType, TypeParameterListSyntax? componentTypeParams = null)
    {
        if (property.GetArgument("NoCommands", false)) {
            return;
        }
        if (property.GetArgument("GenerateSetCommand", true)) {
            GenerateSetCommand(source, property, componentType, componentTypeParams);
        }

        var immutableType = property.ImmutableContainerType;
        if (immutableType == null) { return; }

        var itemName = property.GetArgument("Item", "");
        if (itemName == "") { return; }

        string? keyType, valueType;

        switch (immutableType) {
        case "Dictionary":
            keyType = property.TypeArguments[0];
            valueType = property.TypeArguments[1];

            if (property.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Add" + itemName, $"{keyType} Key, {valueType} Value", ".Add(Key, Value);");
            }
            if (property.GetArgument("GenerateSetItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Set" + itemName, $"{keyType} Key, {valueType} Value", ".SetItem(Key, Value);");
            }
            if (property.GetArgument("GenerateRemoveItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Remove" + itemName, $"{keyType} Key", ".Remove(Key);");
            }
            break;

        case "Queue":
            valueType = property.TypeArguments[0];

            if (property.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Enqueue" + itemName, $"{valueType} Value", ".Enqueue(Value);");
            }
            break;

        case "Stack":
            valueType = property.TypeArguments[0];

            if (property.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Push" + itemName, $"{valueType} Value", ".Push(Value);");
            }
            break;
        
        case "HashSet":
        case "List":
        case "Array":
            valueType = property.TypeArguments[0];

            if (property.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Add" + itemName, $"{valueType} Value", ".Add(Value);");
            }
            if (immutableType != "HashSet" && property.GetArgument("GenerateSetItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Set" + itemName, $"int Index, {valueType} Value", ".SetItem(Index, Value);");
            }
            if (property.GetArgument("GenerateRemoveItemCommand", true)) {
                source.WriteLine();
                GenerateImmutableContainerCommand(
                    source, property, componentType, componentTypeParams,
                    "Remove" + itemName, $"{valueType} Value", ".Remove(Value);");
            }
            break;
        }
    }

    public static void GenerateSetCommand(
        IndentedTextWriter source, in PropertyInfo property, string componentType,
        TypeParameterListSyntax? componentTypeParams = null)
    {
        void WriteComponentType()
        {
            source.Write(componentType);
            if (componentTypeParams != null) {
                WriteTypeParameters(source, componentTypeParams);
            }
        }

        var commandName = "Set" + property.Name;

        source.Write("public readonly record struct ");
        source.Write(commandName);
        source.Write('(');
        source.Write(property.DisplayType);
        source.Write(" Value) : global::Sia.IReconstructableCommand<");
        source.Write(commandName);
        source.Write(">, global::Sia.IParallelCommand, global::Sia.IParallelCommand<");
        WriteComponentType();
        source.WriteLine('>');
        source.WriteLine('{');
        source.Indent++;

        source.Write("public static ");
        source.Write(commandName);
        source.WriteLine(" ReconstructFromCurrentState(in global::Sia.EntityRef entity)");
        source.Indent++;
        source.Write("=> new(entity.Get<");
        WriteComponentType();
        source.Write(">().");
        source.Write(property.Name);
        source.WriteLine(");");
        source.Indent--;

        source.WriteLine("public void Execute(global::Sia.World _, in global::Sia.EntityRef target)");
        source.Indent++;
        source.WriteLine("=> ExecuteOnParallel(target);");
        source.Indent--;

        source.Write("public void Execute(global::Sia.World _, in global::Sia.EntityRef target, ref ");
        WriteComponentType();
        source.WriteLine(" component)");
        source.Indent++;
        source.WriteLine("=> ExecuteOnParallel(ref component);");
        source.Indent--;

        source.WriteLine("public void ExecuteOnParallel(in global::Sia.EntityRef target)");
        source.Indent++;
        source.Write("=> target.Get<");
        WriteComponentType();
        source.Write(">().");
        source.Write(property.Name);
        source.WriteLine(" = Value;");
        source.Indent--;

        source.Write("public void ExecuteOnParallel(ref ");
        WriteComponentType();
        source.WriteLine(" component)");
        source.Indent++;
        source.Write("=> component.");
        source.Write(property.Name);
        source.WriteLine(" = Value;");
        source.Indent--;

        source.Indent--;
        source.WriteLine("}");
    }

    public static void GenerateImmutableContainerCommand(
        IndentedTextWriter source, in PropertyInfo property, string componentType,
        TypeParameterListSyntax? componentTypeParams,
        string commandName, string arguments, string call)
    {
        void WriteComponentType()
        {
            source.Write(componentType);
            if (componentTypeParams != null) {
                WriteTypeParameters(source, componentTypeParams);
            }
        }

        source.Write("public readonly record struct ");
        source.Write(commandName);

        source.Write('(');
        source.Write(arguments);
        source.Write(") : global::Sia.ICommand, global::Sia.ICommand<");
        WriteComponentType();
        source.WriteLine('>');
        source.WriteLine('{');
        source.Indent++;

        source.WriteLine("public void Execute(global::Sia.World _, in global::Sia.EntityRef target)");
        source.WriteLine('{');
        source.Indent++;

        source.Write("ref var component = ref target.Get<");
        WriteComponentType();
        source.WriteLine(">();");

        source.Write("component.");
        source.Write(property.Name);
        source.Write(" = component.");
        source.Write(property.Name);
        source.WriteLine(call);

        source.Indent--;
        source.WriteLine("}");

        source.WriteLine();

        source.Write("public void Execute(global::Sia.World _, in global::Sia.EntityRef target, ref ");
        WriteComponentType();
        source.WriteLine(" component)");
        source.WriteLine('{');
        source.Indent++;

        source.Write("component.");
        source.Write(property.Name);
        source.Write(" = component.");
        source.Write(property.Name);
        source.WriteLine(call);

        source.Indent--;
        source.WriteLine("}");

        source.Indent--;
        source.WriteLine("}");
    }
}