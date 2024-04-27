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
        PropertyInfo Property);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static context => {
            context.AddSource("SiaAttribute.g.cs",
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
                return (syntax, componentType!, parentTypes: GetParentTypes(componentType!));
            })
            .Where(static t => t.parentTypes.All(
                static typeDecl => typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword)))
            .Select(static (t, token) => {
                var (syntax, containtingType, parentTypes) = t;

                var model = syntax.SemanticModel;
                var targetSymbol = model.GetDeclaredSymbol(containtingType, token)!;

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
            }).Collect();
        
        context.RegisterSourceOutput(codeGenInfos, static (context, infos) => {
            var componentTypes = new HashSet<TypeDeclarationSyntax>();

            foreach (var info in infos) {
                using var source = CreateFileSource(out var builder);
                var componentType = info.ComponentType;

                using (GenerateInNamespace(source, info.Namespace)) {
                    using (GenerateInPartialTypes(source, info.ParentTypes.Append(componentType))) {
                        var compTypeStr = componentType.Identifier.ToString();
                        var compTypeParams = componentType.TypeParameterList;

                        if (componentTypes.Add(componentType)) {
                            GenerateViewMainDecl(source, compTypeStr, compTypeParams);
                            source.WriteLine();
                        }

                        GeneratePropertyCommands(
                            source, info.Property, compTypeStr, compTypeParams);
                        
                        source.WriteLine();
                        GenerateViewProperty(source, info.Property);
                    }
                }

                Debug.Assert(source.Indent == 0);
                context.AddSource(GenerateFileName(info), builder.ToString());
            }
        });
    }

    private static bool CheckWritable(SyntaxNode node)
        => node switch {
            PropertyDeclarationSyntax propSyntax =>
                !propSyntax.Modifiers.Any(SyntaxKind.StaticKeyword)
                    && propSyntax.AccessorList!.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)),
            VariableDeclaratorSyntax varSyntax =>
                FindParentNode<FieldDeclarationSyntax>(varSyntax, out var fieldDecl)
                    && !fieldDecl!.Modifiers.Any(m =>
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

    public static void GeneratePropertyCommands(
        IndentedTextWriter source, PropertyInfo info,
        string componentType, TypeParameterListSyntax? componentTypeParams = null)
    {
        if (info.GetArgument("NoCommands", false)) {
            return;
        }
        if (info.GetArgument("GenerateSetCommand", true)) {
            GenerateSetCommand(
                source, info, componentType, componentTypeParams);
        }

        var immutableType = info.ImmutableContainerType;
        if (immutableType == null) { return; }

        var itemName = info.GetArgument("Item", "");
        if (itemName == "") { return; }

        string? keyType, valueType;

        switch (immutableType) {
        case "Dictionary":
            keyType = info.TypeArguments[0];
            valueType = info.TypeArguments[1];

            if (info.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                var command = "Add" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"{keyType} Key, {valueType} Value",
                    ".Add(Key, Value);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"{keyType} key, {valueType} value",
                    "key, value");
            }
            if (info.GetArgument("GenerateSetItemCommand", true)) {
                source.WriteLine();
                var command = "Set" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"{keyType} Key, {valueType} Value",
                    ".SetItem(Key, Value);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"{keyType} key, {valueType} value",
                    "key, value");
            }
            if (info.GetArgument("GenerateRemoveItemCommand", true)) {
                source.WriteLine();
                var command = "Remove" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"{keyType} Key",
                    ".Remove(Key);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"{keyType} key",
                    "key");
            }
            break;

        case "Queue":
            valueType = info.TypeArguments[0];

            if (info.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                var command = "Enqueue" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} Value",
                    ".Enqueue(Value);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} value",
                    "value");
            }
            break;

        case "Stack":
            valueType = info.TypeArguments[0];

            if (info.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                var command = "Push" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} Value",
                    ".Push(Value);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} value",
                    "value");
            }
            break;
        
        case "HashSet":
        case "List":
        case "Array":
            valueType = info.TypeArguments[0];
            if (info.GetArgument("GenerateAddItemCommand", true)) {
                source.WriteLine();
                var command = "Add" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} Value",
                    ".Add(Value);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} value",
                    "value");
            }
            if (immutableType != "HashSet" && info.GetArgument("GenerateSetItemCommand", true)) {
                source.WriteLine();
                var command = "Set" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"int Index, {valueType} Value",
                    ".SetItem(Index, Value);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"int index, {valueType} value",
                    "index, value");
            }
            if (info.GetArgument("GenerateRemoveItemCommand", true)) {
                source.WriteLine();
                var command = "Remove" + itemName;
                GenerateImmutableContainerCommand(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} Value",
                    ".Remove(Value);");
                source.WriteLine();
                GenerateImmutableContainerViewMethods(
                    source, info, componentType, componentTypeParams, command,
                    $"{valueType} value",
                    "value");
            }
            break;
        }
    }

    public static void GenerateSetCommand(
        IndentedTextWriter source, PropertyInfo info,
        string componentType, TypeParameterListSyntax? componentTypeParams = null)
    {
        void WriteComponentType()
        {
            source.Write(componentType);
            if (componentTypeParams != null) {
                WriteTypeParameters(source, componentTypeParams);
            }
        }

        var commandName = "Set" + info.Name;

        source.Write("public readonly record struct ");
        source.Write(commandName);
        source.Write('(');
        source.Write(info.DisplayType);
        source.Write(" Value) : global::Sia.IReconstructableCommand<");
        source.Write(commandName);
        source.Write(">, global::Sia.IPureCommand<");
        WriteComponentType();
        source.Write(">, global::Sia.IPropertyCommand<");
        source.Write(info.DisplayType);
        source.WriteLine('>');
        source.WriteLine('{');
        source.Indent++;


        source.Write("public static string PropertyName => \"");
        source.Write(info.Name);
        source.WriteLine("\";");

        source.Write("public static ");
        source.Write(commandName);
        source.WriteLine(" ReconstructFromCurrentState(in global::Sia.EntityRef entity)");
        source.Indent++;
        source.Write("=> new(entity.Get<");
        WriteComponentType();
        source.Write(">().");
        source.Write(info.Name);
        source.WriteLine(");");
        source.Indent--;

        source.WriteLine("public void Execute(global::Sia.World _, in global::Sia.EntityRef target)");
        source.Indent++;
        source.Write("=> Execute(ref target.Get<");
        WriteComponentType();
        source.WriteLine(">());");
        source.Indent--;

        source.Write("public void Execute(global::Sia.World _, in global::Sia.EntityRef target, ref ");
        WriteComponentType();
        source.WriteLine(" component)");
        source.Indent++;
        source.WriteLine("=> Execute(ref component);");
        source.Indent--;

        source.Write("public void Execute(ref ");
        WriteComponentType();
        source.WriteLine(" component)");
        source.Indent++;
        source.Write("=> component.");
        source.Write(info.Name);
        source.WriteLine(" = Value;");
        source.Indent--;

        source.Indent--;
        source.WriteLine("}");
    }

    public static void GenerateImmutableContainerCommand(
        IndentedTextWriter source, PropertyInfo info,
        string componentType, TypeParameterListSyntax? componentTypeParams,
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
        source.Write(") : global::Sia.ICommand<");
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
        source.Write(info.Name);
        source.Write(" = component.");
        source.Write(info.Name);
        source.WriteLine(call);

        source.Indent--;
        source.WriteLine("}");

        source.WriteLine();

        source.Write("public void Execute(global::Sia.World _, in global::Sia.EntityRef target, ref ");
        WriteComponentType();
        source.WriteLine(" component)");

        source.Indent++;
        source.Write("=> component.");
        source.Write(info.Name);
        source.Write(" = component.");
        source.Write(info.Name);
        source.WriteLine(call);
        source.Indent--;

        source.Indent--;
        source.WriteLine("}");
    }

    public static void GenerateViewMainDecl(
        IndentedTextWriter source, string componentType, TypeParameterListSyntax? componentTypeParams = null)
    {
        void WriteComponentType()
        {
            source.Write(componentType);
            if (componentTypeParams != null) {
                WriteTypeParameters(source, componentTypeParams);
            }
        }
        
        source.WriteLine("public readonly ref partial struct View(global::Sia.EntityRef entity, global::Sia.World? world = null)");
        source.WriteLine("{");
        source.Indent++;

        source.WriteLine("private readonly global::Sia.World _world = world ?? global::Sia.Context<global::Sia.World>.Current!;");
        source.Write("private readonly ref ");
        WriteComponentType();
        source.Write(" _component = ref entity.Get<");
        WriteComponentType();
        source.WriteLine(">();");

        source.WriteLine();
        source.WriteLine("public readonly global::Sia.World GetWorld() => _world;");
        source.Write("public readonly ref ");
        WriteComponentType();
        source.WriteLine(" GetRef() => ref _component;");

        source.Indent--;
        source.WriteLine("}");
    }

    public static void GenerateViewProperty(
        IndentedTextWriter source, PropertyInfo info, TypeParameterListSyntax? componentTypeParams = null)
    {
        source.WriteLine("public readonly ref partial struct View");
        source.WriteLine("{");
        source.Indent++;

        source.Write("public ");
        source.Write(info.Type);
        source.Write(" ");
        source.Write(info.Name);
        source.WriteLine(" {");

        source.Indent++;
        source.Write("get => _component.");
        source.Write(info.Name);
        source.WriteLine(';');
        if (!info.GetArgument("NoCommands", false) && info.GetArgument("GenerateSetCommand", true)) {
            source.WriteLine("set {");
            source.Indent++;
            source.Write("_component.");
            source.Write(info.Name);
            source.WriteLine(" = value;");
            source.Write("_world.Dispatcher.Send(entity, new Set");
            source.Write(info.Name);
            source.WriteLine("(value));");
            source.Indent--;
            source.WriteLine('}');
        }
        else {
            source.Write("set => _component.");
            source.Write(info.Name);
            source.WriteLine(" = value;");
        }
        source.Indent--;
        source.WriteLine("}");

        source.Indent--;
        source.WriteLine("}");
    }

    public static void GenerateImmutableContainerViewMethods(
        IndentedTextWriter source, PropertyInfo info,
        string componentType, TypeParameterListSyntax? componentTypeParams,
        string commandName, string arguments, string parameters)
    {
        source.WriteLine("public readonly ref partial struct View");
        source.WriteLine("{");
        source.Indent++;

        source.Write("public void ");
        source.Write(commandName);
        source.Write('(');
        source.Write(arguments);
        source.WriteLine(')');

        source.Indent++;
        source.Write("=> _world.Modify(entity, ref _component, new ");
        source.Write(componentType);
        if (componentTypeParams != null) {
            WriteTypeParameters(source, componentTypeParams);
        }
        source.Write(".");
        source.Write(commandName);
        source.Write('(');
        source.Write(parameters);
        source.WriteLine("));");
        source.Indent--;

        source.Indent--;
        source.WriteLine("}");
    }
}