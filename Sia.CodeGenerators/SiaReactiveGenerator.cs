namespace Sia.CodeGenerators;

using System.CodeDom.Compiler;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using static Common;

[Generator]
public partial class SiaReactiveGenerator : IIncrementalGenerator
{
    private const string ReactiveComponentAttributeName =
        "Sia.Reactive.ReactiveComponentAttribute";

    private const string ComponentsTrackingName = "Components";

    private static readonly DiagnosticDescriptor InvalidContainer = new(
        id: "SIA100",
        title: "Reactive component container must be static and partial",
        messageFormat: "Reactive component container '{0}' must be a static partial class",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidContractMember = new(
        id: "SIA101",
        title: "Reactive component must define its contract members",
        messageFormat: "Reactive component '{0}' must define {1}",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private const string InitialStateRequirement =
        "exactly one public instance extension property 'InitialState' on a scoped-in readonly record struct receiver";
    private const string ReducerRequirement =
        "exactly one public instance extension method 'Reduce(message)' whose receiver and return type are the InitialState type";
    private const string RendererRequirement =
        "exactly one public instance extension method 'Render(state)' on the props receiver and return Sia.Reactive.ReactiveNode";

    private static readonly DiagnosticDescriptor InvalidDataModel = new(
        id: "SIA102",
        title: "Reactive component data must be immutable records",
        messageFormat: "Reactive component '{0}' requires readonly record struct props/state and a record message type",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NonStaticCallback = new(
        id: "SIA103",
        title: "Reactive nested callbacks must be static",
        messageFormat: "Reactive callback '{0}' must be a static lambda or static method group",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AmbientStateAccess = new(
        id: "SIA104",
        title: "Reactive pure functions cannot access ambient state",
        messageFormat: "Reactive function '{0}' cannot access Sia.Context<T>; pass the value through props or Reactive.Provide/Use",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidEventMessage = new(
        id: "SIA105",
        title: "Reactive event message must match the component reducer",
        messageFormat: "Reactive.On returns message type '{0}', which is not assignable to component message type '{1}'",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private readonly record struct ExtensionMember(
        ExtensionBlockDeclarationSyntax Block,
        MemberDeclarationSyntax Member,
        ITypeSymbol Receiver,
        SemanticModel Model);

    private readonly record struct PartialTypeShape(
        bool IsStatic,
        string Keyword,
        string Name,
        ImmutableArray<string> TypeParameters);

    private readonly record struct ComponentInfo(
        string? Namespace,
        PartialTypeShape Declaration,
        ImmutableArray<PartialTypeShape> ParentTypes,
        string ComponentType,
        string DisplayName,
        string? PropsType,
        string? StateType,
        string? MessageType,
        Location? Location,
        bool ContainerValid,
        bool InitialValid,
        bool ReducerValid,
        bool RendererValid,
        bool DataModelValid,
        ImmutableArray<(Location Location, string Callback)> NonStaticCallbacks,
        ImmutableArray<(Location Location, string Function)> AmbientAccesses,
        ImmutableArray<(Location Location, string Actual, string Expected)>
            InvalidEventMessages)
    {
        public bool IsValid
            => ContainerValid
                && InitialValid
                && ReducerValid
                && RendererValid
                && DataModelValid
                && NonStaticCallbacks.IsEmpty
                && AmbientAccesses.IsEmpty
                && InvalidEventMessages.IsEmpty;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var components = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ReactiveComponentAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attribute, token) => CreateInfo(
                    (TypeDeclarationSyntax)attribute.TargetNode,
                    (INamedTypeSymbol)attribute.TargetSymbol,
                    attribute.SemanticModel.Compilation,
                    token))
            .WithTrackingName(ComponentsTrackingName);

        context.RegisterSourceOutput(components, static (output, info) => {
            ReportDiagnostics(output, info);
            if (!info.IsValid) {
                return;
            }
            output.AddSource(GenerateFileName(info), GenerateComponent(info));
        });
    }

    private static ComponentInfo CreateInfo(
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol symbol,
        Compilation compilation,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var members = ImmutableArray.CreateBuilder<ExtensionMember>();
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences) {
            token.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(token) is not TypeDeclarationSyntax part) {
                continue;
            }
            var model = compilation.GetSemanticModel(part.SyntaxTree);
            foreach (var block in part.Members
                         .OfType<ExtensionBlockDeclarationSyntax>()) {
                if (!TryGetReceiver(block, model, token, out var receiver)) {
                    continue;
                }
                foreach (var member in block.Members) {
                    members.Add(new(block, member, receiver, model));
                }
            }
        }

        var initialCandidates = members
            .Where(static member => member.Member is PropertyDeclarationSyntax {
                Identifier.ValueText: "InitialState"
            })
            .ToImmutableArray();

        ITypeSymbol? props = null;
        ITypeSymbol? state = null;
        var initialValid = initialCandidates.Length == 1;
        if (initialValid) {
            var candidate = initialCandidates[0];
            var property = (PropertyDeclarationSyntax)candidate.Member;
            initialValid = IsPublicInstance(property.Modifiers)
                && IsScopedIn(candidate.Block)
                && IsGetterOnly(property)
                && (state = candidate.Model.GetTypeInfo(
                    property.Type, token).Type) is not null;
            props = candidate.Receiver;
        }

        var reducerCandidates = members
            .Where(static member => member.Member is MethodDeclarationSyntax {
                Identifier.ValueText: "Reduce"
            })
            .ToImmutableArray();

        ITypeSymbol? message = null;
        var reducerValid = reducerCandidates.Length == 1;
        if (reducerValid) {
            var candidate = reducerCandidates[0];
            var method = (MethodDeclarationSyntax)candidate.Member;
            reducerValid = IsPublicInstance(method.Modifiers)
                && IsScopedIn(candidate.Block)
                && method.TypeParameterList is null
                && method.ParameterList.Parameters.Count == 1
                && IsScopedIn(method.ParameterList.Parameters[0])
                && state is not null
                && SymbolEqualityComparer.Default.Equals(
                    candidate.Receiver, state)
                && SymbolEqualityComparer.Default.Equals(
                    candidate.Model.GetTypeInfo(
                        method.ReturnType, token).Type,
                    state);
            if (reducerValid) {
                message = candidate.Model.GetTypeInfo(
                    method.ParameterList.Parameters[0].Type!, token).Type;
                reducerValid = message is not null;
            }
        }

        var rendererCandidates = members
            .Where(static member => member.Member is MethodDeclarationSyntax {
                Identifier.ValueText: "Render"
            })
            .ToImmutableArray();

        var rendererValid = rendererCandidates.Length == 1;
        if (rendererValid) {
            var candidate = rendererCandidates[0];
            var method = (MethodDeclarationSyntax)candidate.Member;
            rendererValid = IsPublicInstance(method.Modifiers)
                && IsScopedIn(candidate.Block)
                && method.TypeParameterList is null
                && method.ParameterList.Parameters.Count == 1
                && IsScopedIn(method.ParameterList.Parameters[0])
                && props is not null
                && state is not null
                && SymbolEqualityComparer.Default.Equals(
                    candidate.Receiver, props)
                && SymbolEqualityComparer.Default.Equals(
                    candidate.Model.GetTypeInfo(
                        method.ParameterList.Parameters[0].Type!, token).Type,
                    state)
                && candidate.Model.GetTypeInfo(method.ReturnType, token).Type
                    ?.ToDisplayString() == "Sia.Reactive.ReactiveNode";
        }

        var containerValid = declaration is ClassDeclarationSyntax
            && declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword);
        var dataModelValid = IsReadonlyRecordStruct(props)
            && IsReadonlyRecordStruct(state)
            && message is INamedTypeSymbol { IsRecord: true };
        var nonStaticCallbacks = FindNonStaticCallbacks(
            symbol, compilation, token);
        var ambientAccesses = FindAmbientAccesses(members, token);
        var invalidEventMessages = FindInvalidEventMessages(
            symbol, compilation, message, token);

        return new(
            symbol.ContainingNamespace.IsGlobalNamespace
                ? null : symbol.ContainingNamespace.ToDisplayString(),
            CreatePartialTypeShape(declaration),
            [.. GetParentTypes(declaration).Select(CreatePartialTypeShape)],
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ToDisplayString(),
            props?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            state?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            message?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            declaration.Identifier.GetLocation(),
            containerValid,
            initialValid,
            reducerValid,
            rendererValid,
            dataModelValid,
            nonStaticCallbacks,
            ambientAccesses,
            invalidEventMessages);
    }

    private static PartialTypeShape CreatePartialTypeShape(TypeDeclarationSyntax typeDecl)
    {
        var keyword = typeDecl.Kind() switch
        {
            SyntaxKind.ClassDeclaration => "partial class",
            SyntaxKind.StructDeclaration => "partial struct",
            SyntaxKind.RecordDeclaration => "partial record",
            SyntaxKind.RecordStructDeclaration => "partial record struct",
            _ => "partial class"
        };
        var typeParameters = typeDecl.TypeParameterList is { } typeParams
            ? typeParams.Parameters
                .Select(static p => p.Identifier.ToString())
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
        return new(
            typeDecl.Modifiers.Any(SyntaxKind.StaticKeyword),
            keyword,
            typeDecl.Identifier.ToString(),
            typeParameters);
    }

    private static bool TryGetReceiver(
        ExtensionBlockDeclarationSyntax block,
        SemanticModel model,
        CancellationToken token,
        out ITypeSymbol receiver)
    {
        if (block.ParameterList is not { } parameterList) {
            receiver = null!;
            return false;
        }
        var parameters = parameterList.Parameters;
        if (parameters.Count != 1
            || parameters[0].Identifier.IsMissing
            || parameters[0].Type is null
            || model.GetTypeInfo(parameters[0].Type!, token).Type is not { } type) {
            receiver = null!;
            return false;
        }
        receiver = type;
        return true;
    }

    private static bool IsPublicInstance(SyntaxTokenList modifiers)
        => modifiers.Any(SyntaxKind.PublicKeyword)
            && !modifiers.Any(SyntaxKind.StaticKeyword);

    private static bool IsGetterOnly(PropertyDeclarationSyntax property)
        => property.ExpressionBody is not null
            || property.AccessorList is {
                Accessors.Count: 1
            } accessors
            && accessors.Accessors[0].IsKind(
                SyntaxKind.GetAccessorDeclaration);

    private static bool IsScopedIn(ExtensionBlockDeclarationSyntax block)
        => block.ParameterList is { Parameters.Count: 1 } parameterList
            && IsScopedIn(parameterList.Parameters[0]);

    private static bool IsScopedIn(ParameterSyntax parameter)
        => parameter.Modifiers.Any(SyntaxKind.ScopedKeyword)
            && parameter.Modifiers.Any(SyntaxKind.InKeyword);

    private static bool IsReadonlyRecordStruct(ITypeSymbol? type)
        => type is INamedTypeSymbol {
            IsRecord: true,
            IsValueType: true,
            IsReadOnly: true
        };

    private static ImmutableArray<(Location Location, string Callback)>
        FindNonStaticCallbacks(
            INamedTypeSymbol component,
            Compilation compilation,
            CancellationToken token)
    {
        var invalid = ImmutableArray.CreateBuilder<(
            Location Location, string Callback)>();
        foreach (var syntaxReference in component.DeclaringSyntaxReferences) {
            token.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(token) is not TypeDeclarationSyntax part) {
                continue;
            }
            var model = compilation.GetSemanticModel(part.SyntaxTree);
            foreach (var invocation in part.DescendantNodes()
                         .OfType<InvocationExpressionSyntax>()) {
                if (model.GetOperation(invocation, token)
                    is not IInvocationOperation operation
                    || operation.TargetMethod.ContainingType.ToDisplayString()
                        != "Sia.Reactive.Reactive") {
                    continue;
                }
                foreach (var argument in operation.Arguments) {
                    if (argument.Parameter is not { } parameter
                        || !IsNestedCallback(parameter)) {
                        continue;
                    }
                    var expression = argument.Syntax is ArgumentSyntax syntax
                        ? syntax.Expression
                        : argument.Value.Syntax as ExpressionSyntax;
                    if (expression is null
                        || IsStaticCallback(expression, model, token)) {
                        continue;
                    }
                    invalid.Add((
                        expression.GetLocation(),
                        $"{operation.TargetMethod.Name}.{parameter.Name}"));
                }
            }
        }
        return invalid.ToImmutable();
    }

    private static bool IsNestedCallback(IParameterSymbol parameter)
        => parameter.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString()
                == "Sia.Reactive.NestedCallbackAttribute");

    private static bool IsStaticCallback(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken token)
    {
        if (expression is AnonymousFunctionExpressionSyntax anonymous) {
            return anonymous.Modifiers.Any(SyntaxKind.StaticKeyword);
        }
        return model.GetSymbolInfo(expression, token).Symbol
            is IMethodSymbol { IsStatic: true };
    }

    private static ImmutableArray<(Location Location, string Function)>
        FindAmbientAccesses(
            ImmutableArray<ExtensionMember>.Builder members,
            CancellationToken token)
    {
        var invalid = ImmutableArray.CreateBuilder<(
            Location Location, string Function)>();
        foreach (var member in members) {
            var function = GetContractMemberName(member.Member);
            if (function is null) {
                continue;
            }
            foreach (var node in member.Member.DescendantNodes()) {
                var symbol = member.Model.GetSymbolInfo(node, token).Symbol;
                if (symbol?.ContainingType is not INamedTypeSymbol containing
                    || containing.OriginalDefinition.ToDisplayString()
                        != "Sia.Context<T>") {
                    continue;
                }
                invalid.Add((node.GetLocation(), function));
                break;
            }
        }
        return invalid.ToImmutable();
    }

    private static ImmutableArray<(
        Location Location,
        string Actual,
        string Expected)> FindInvalidEventMessages(
        INamedTypeSymbol component,
        Compilation compilation,
        ITypeSymbol? componentMessage,
        CancellationToken token)
    {
        if (componentMessage is null) {
            return [];
        }

        var invalid = ImmutableArray.CreateBuilder<(
            Location Location,
            string Actual,
            string Expected)>();
        foreach (var syntaxReference in component.DeclaringSyntaxReferences) {
            token.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(token) is not TypeDeclarationSyntax part) {
                continue;
            }
            var model = compilation.GetSemanticModel(part.SyntaxTree);
            foreach (var invocation in part.DescendantNodes()
                         .OfType<InvocationExpressionSyntax>()) {
                if (model.GetOperation(invocation, token)
                    is not IInvocationOperation {
                        TargetMethod.Name: "On"
                    } operation
                    || operation.TargetMethod.ContainingType.ToDisplayString()
                        != "Sia.Reactive.Reactive"
                    || operation.TargetMethod.TypeArguments.Length != 2) {
                    continue;
                }

                var actual = operation.TargetMethod.TypeArguments[1];
                if (compilation.ClassifyConversion(
                        actual, componentMessage).IsImplicit) {
                    continue;
                }
                invalid.Add((
                    invocation.GetLocation(),
                    actual.ToDisplayString(),
                    componentMessage.ToDisplayString()));
            }
        }
        return invalid.ToImmutable();
    }

    private static string? GetContractMemberName(MemberDeclarationSyntax member)
        => member switch
        {
            PropertyDeclarationSyntax { Identifier.ValueText: "InitialState" }
                => "InitialState",
            MethodDeclarationSyntax { Identifier.ValueText: "Reduce" }
                => "Reduce",
            MethodDeclarationSyntax { Identifier.ValueText: "Render" }
                => "Render",
            _ => null
        };

    private static void ReportDiagnostics(
        SourceProductionContext output,
        in ComponentInfo info)
    {
        if (!info.ContainerValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidContainer, info.Location, info.DisplayName));
        }
        if (!info.InitialValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidContractMember, info.Location, info.DisplayName,
                InitialStateRequirement));
        }
        if (!info.ReducerValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidContractMember, info.Location, info.DisplayName,
                ReducerRequirement));
        }
        if (!info.RendererValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidContractMember, info.Location, info.DisplayName,
                RendererRequirement));
        }
        if (!info.DataModelValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidDataModel, info.Location, info.DisplayName));
        }
        foreach (var (location, callback) in info.NonStaticCallbacks) {
            output.ReportDiagnostic(Diagnostic.Create(
                NonStaticCallback, location, callback));
        }
        foreach (var (location, function) in info.AmbientAccesses) {
            output.ReportDiagnostic(Diagnostic.Create(
                AmbientStateAccess, location, function));
        }
        foreach (var (location, actual, expected) in info.InvalidEventMessages) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidEventMessage, location, actual, expected));
        }
    }

    private static string GenerateComponent(in ComponentInfo info)
    {
        using var source = CreateFileSource(out var builder);
        source.WriteLine("#nullable enable");
        source.WriteLine();
        using (WriteNamespace(source, info.Namespace)) {
            using (WriteContainerChain(source, info.ParentTypes)) {
                if (info.Declaration.IsStatic) {
                    source.Write("static ");
                }
                source.Write(info.Declaration.Keyword);
                source.Write(' ');
                WriteTypeShape(source, info.Declaration);
                source.WriteLine();
                source.WriteLine("{");
                source.Indent++;
                source.WriteLine($"[{Common.GeneratedCodeAttribute}]");
                source.Write("public static global::Sia.Reactive.ReactiveComponent<");
                source.Write(info.PropsType);
                source.Write(", ");
                source.Write(info.StateType);
                source.Write(", ");
                source.Write(info.MessageType);
                source.WriteLine("> Definition { get; }");
                source.Indent++;
                source.WriteLine("= global::Sia.Reactive.Reactive.Component(");
                source.Indent++;
                source.Write("initial: static (scoped in ");
                source.Write(info.PropsType);
                source.Write(" props) => ");
                source.Write(info.ComponentType);
                source.WriteLine(".get_InitialState(props),");
                source.Write("reduce: static (scoped in ");
                source.Write(info.StateType);
                source.Write(" state, scoped in ");
                source.Write(info.MessageType);
                source.Write(" message) => ");
                source.Write(info.ComponentType);
                source.WriteLine(".Reduce(state, message),");
                source.Write("render: static (scoped in ");
                source.Write(info.PropsType);
                source.Write(" props, scoped in ");
                source.Write(info.StateType);
                source.Write(" state) => ");
                source.Write(info.ComponentType);
                source.WriteLine(".Render(props, state));");
                source.Indent -= 2;
                source.Indent--;
                source.WriteLine("}");
            }
        }
        return builder.ToString();
    }

    private static IDisposable WriteNamespace(IndentedTextWriter source, string? ns)
    {
        if (ns is null) {
            return EmptyDisposable.Instance;
        }
        source.Write("namespace ");
        source.WriteLine(ns);
        source.WriteLine("{");
        source.Indent++;
        return new EnclosingDisposable(source, 1);
    }

    private static IDisposable WriteContainerChain(
        IndentedTextWriter source, ImmutableArray<PartialTypeShape> shapes)
    {
        foreach (var shape in shapes) {
            if (shape.IsStatic) {
                source.Write("static ");
            }
            source.Write(shape.Keyword);
            source.Write(' ');
            WriteTypeShape(source, shape);
            source.WriteLine();
            source.WriteLine("{");
            source.Indent++;
        }
        return shapes.Length != 0
            ? new EnclosingDisposable(source, shapes.Length)
            : EmptyDisposable.Instance;
    }

    private static void WriteTypeShape(IndentedTextWriter source, PartialTypeShape shape)
    {
        source.Write(shape.Name);
        if (shape.TypeParameters.IsEmpty) {
            return;
        }
        source.Write('<');
        for (var index = 0; index < shape.TypeParameters.Length; index++) {
            if (index != 0) {
                source.Write(", ");
            }
            source.Write(shape.TypeParameters[index]);
        }
        source.Write('>');
    }

    private static string GenerateFileName(in ComponentInfo info)
        => info.ComponentType
            .Replace("global::", string.Empty)
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            + ".Reactive.g.cs";
}
