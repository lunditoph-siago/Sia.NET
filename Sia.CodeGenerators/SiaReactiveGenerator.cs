namespace Sia.CodeGenerators;

using System.CodeDom.Compiler;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using static Common;

[Generator]
public sealed class SiaReactiveGenerator : IIncrementalGenerator
{
    private const string ReactiveComponentAttributeName =
        "Sia.Reactive.ReactiveComponentAttribute";
    private const string ComponentsTrackingName = "Components";
    private const string RendererRequirement =
        "exactly one public static method 'Render(in props, ref Hooks)' returning Sia.Reactive.ReactiveNode";

    private static readonly DiagnosticDescriptor InvalidContainer = new(
        id: "SIA100",
        title: "Reactive component container must be static and partial",
        messageFormat: "Reactive component container '{0}' must be a static partial class",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidRenderer = new(
        id: "SIA101",
        title: "Reactive component must define its renderer",
        messageFormat: "Reactive component '{0}' must define {1}",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidProps = new(
        id: "SIA102",
        title: "Reactive component props must be immutable",
        messageFormat: "Reactive component '{0}' requires readonly record struct props",
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
        title: "Reactive renderers cannot access ambient state",
        messageFormat: "Reactive renderer '{0}' cannot access Sia.Context<T>; pass the value through props or Reactive.Provide/Use",
        category: "Sia.Reactive",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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
        Location? Location,
        bool ContainerValid,
        bool RendererValid,
        bool PropsValid,
        ImmutableArray<(Location Location, string Callback)> NonStaticCallbacks,
        ImmutableArray<(Location Location, string Function)> AmbientAccesses)
    {
        public bool IsValid
            => ContainerValid
                && RendererValid
                && PropsValid
                && NonStaticCallbacks.IsEmpty
                && AmbientAccesses.IsEmpty;
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
            if (info.IsValid) {
                output.AddSource(GenerateFileName(info), GenerateComponent(info));
            }
        });
    }

    private static ComponentInfo CreateInfo(
        TypeDeclarationSyntax declaration,
        INamedTypeSymbol symbol,
        Compilation compilation,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var candidates = symbol.GetMembers("Render")
            .OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary)
            .ToImmutableArray();
        var renderer = candidates.Length == 1 ? candidates[0] : null;
        var props = renderer is { Parameters.Length: > 0 }
            ? renderer.Parameters[0].Type
            : null;
        var rendererValid = renderer is {
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                IsGenericMethod: false,
                Parameters.Length: 2
            }
            && renderer.Parameters[0].RefKind == RefKind.In
            && renderer.Parameters[1].RefKind == RefKind.Ref
            && renderer.Parameters[1].Type.ToDisplayString() == "Sia.Reactive.Hooks"
            && renderer.ReturnType.ToDisplayString() == "Sia.Reactive.ReactiveNode";

        var containerValid = declaration is ClassDeclarationSyntax
            && declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword);

        return new(
            symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString(),
            CreatePartialTypeShape(declaration),
            [.. GetParentTypes(declaration).Select(CreatePartialTypeShape)],
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ToDisplayString(),
            props?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            declaration.Identifier.GetLocation(),
            containerValid,
            rendererValid,
            IsReadonlyRecordStruct(props),
            FindNonStaticCallbacks(symbol, compilation, token),
            FindAmbientAccesses(renderer, compilation, token));
    }

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
                    is not IInvocationOperation operation) {
                    continue;
                }
                var containingType = operation.TargetMethod.ContainingType
                    .ToDisplayString();
                if (containingType != "Sia.Reactive.Reactive"
                    && containingType != "Sia.Reactive.Hooks") {
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
            IMethodSymbol? method,
            Compilation compilation,
            CancellationToken token)
    {
        if (method is null) {
            return [];
        }
        var invalid = ImmutableArray.CreateBuilder<(
            Location Location, string Function)>();
        foreach (var syntaxReference in method.DeclaringSyntaxReferences) {
            token.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(token)
                is not MethodDeclarationSyntax declaration) {
                continue;
            }
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var node in declaration.DescendantNodes()) {
                var symbol = model.GetSymbolInfo(node, token).Symbol;
                if (symbol?.ContainingType is not INamedTypeSymbol containing
                    || containing.OriginalDefinition.ToDisplayString()
                        != "Sia.Context<T>") {
                    continue;
                }
                invalid.Add((node.GetLocation(), "Render"));
                break;
            }
        }
        return invalid.ToImmutable();
    }

    private static bool IsReadonlyRecordStruct(ITypeSymbol? type)
        => type is INamedTypeSymbol {
            IsRecord: true,
            IsValueType: true,
            IsReadOnly: true
        };

    private static void ReportDiagnostics(
        SourceProductionContext output,
        in ComponentInfo info)
    {
        if (!info.ContainerValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidContainer, info.Location, info.DisplayName));
        }
        if (!info.RendererValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidRenderer,
                info.Location,
                info.DisplayName,
                RendererRequirement));
        }
        if (!info.PropsValid) {
            output.ReportDiagnostic(Diagnostic.Create(
                InvalidProps, info.Location, info.DisplayName));
        }
        foreach (var (location, callback) in info.NonStaticCallbacks) {
            output.ReportDiagnostic(Diagnostic.Create(
                NonStaticCallback, location, callback));
        }
        foreach (var (location, function) in info.AmbientAccesses) {
            output.ReportDiagnostic(Diagnostic.Create(
                AmbientStateAccess, location, function));
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
                source.WriteLine("> Definition { get; }");
                source.Indent++;
                source.Write("= static (in ");
                source.Write(info.PropsType);
                source.Write(" props, ref global::Sia.Reactive.Hooks hooks) => ");
                source.Write(info.ComponentType);
                source.WriteLine(".Render(props, ref hooks);");
                source.Indent--;
                source.Indent--;
                source.WriteLine("}");
            }
        }
        return builder.ToString();
    }

    private static PartialTypeShape CreatePartialTypeShape(
        TypeDeclarationSyntax declaration)
    {
        var keyword = declaration.Kind() switch {
            SyntaxKind.ClassDeclaration => "partial class",
            SyntaxKind.StructDeclaration => "partial struct",
            SyntaxKind.RecordDeclaration => "partial record",
            SyntaxKind.RecordStructDeclaration => "partial record struct",
            _ => "partial class"
        };
        var typeParameters = declaration.TypeParameterList is { } parameters
            ? parameters.Parameters
                .Select(static parameter => parameter.Identifier.ToString())
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
        return new(
            declaration.Modifiers.Any(SyntaxKind.StaticKeyword),
            keyword,
            declaration.Identifier.ToString(),
            typeParameters);
    }

    private static IDisposable WriteNamespace(
        IndentedTextWriter source,
        string? ns)
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
        IndentedTextWriter source,
        ImmutableArray<PartialTypeShape> shapes)
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
        return shapes.Length == 0
            ? EmptyDisposable.Instance
            : new EnclosingDisposable(source, shapes.Length);
    }

    private static void WriteTypeShape(
        IndentedTextWriter source,
        PartialTypeShape shape)
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
