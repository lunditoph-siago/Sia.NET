namespace Sia.CodeGenerators;

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
internal partial class SiaSystemGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor SystemMustImplementISystem = new(
        id: "SIA_SYSTEM001",
        title: "Sia system must implement ISystem",
        messageFormat: "Type '{0}' is marked with [SiaSystem] but does not implement Sia.ISystem",
        category: "Sia.Systems",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private readonly record struct SystemInfo(
        string Type,
        string DisplayName,
        bool IsValid,
        Location? Location,
        ImmutableArray<string> InSets,
        ImmutableArray<string> BeforeSystems,
        ImmutableArray<string> AfterSystems,
        ImmutableArray<string> BeforeSets,
        ImmutableArray<string> AfterSets);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static context => {
            context.AddSource("SiaSystemAttribute.g.cs",
                SourceText.From(SiaSystemAttributeSource, Encoding.UTF8));
        });

        var systems = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SiaSystemAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (context, token) => {
                    var symbol = (INamedTypeSymbol)context.TargetSymbol;
                    return CreateSystemInfo(symbol, token);
                })
            .Collect();

        context.RegisterSourceOutput(systems, static (context, infos) => {
            if (infos.IsDefaultOrEmpty) {
                return;
            }

            foreach (var info in infos) {
                if (!info.IsValid) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SystemMustImplementISystem,
                        info.Location,
                        info.DisplayName));
                }
            }

            var validInfos = SortAndDistinct(infos);

            if (validInfos.IsDefaultOrEmpty) {
                return;
            }

            var source = GenerateProvider(validInfos);
            context.AddSource("SiaSystemDescriptorProvider.g.cs", source);
        });
    }

    private static SystemInfo CreateSystemInfo(
        INamedTypeSymbol symbol,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var inSets = ImmutableArray.CreateBuilder<string>();
        var beforeSystems = ImmutableArray.CreateBuilder<string>();
        var afterSystems = ImmutableArray.CreateBuilder<string>();
        var beforeSets = ImmutableArray.CreateBuilder<string>();
        var afterSets = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in symbol.GetAttributes()) {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null || !attributeClass.IsGenericType) {
                continue;
            }

            var definition = attributeClass.ConstructedFrom.ToDisplayString();
            var target = attributeClass.TypeArguments[0]
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            switch (definition) {
                case "Sia.SiaInSetAttribute<TSet>":
                    inSets.Add(target);
                    break;
                case "Sia.SiaBeforeAttribute<TSystem>":
                    beforeSystems.Add(target);
                    break;
                case "Sia.SiaAfterAttribute<TSystem>":
                    afterSystems.Add(target);
                    break;
                case "Sia.SiaBeforeSetAttribute<TSet>":
                    beforeSets.Add(target);
                    break;
                case "Sia.SiaAfterSetAttribute<TSet>":
                    afterSets.Add(target);
                    break;
            }
        }

        return new(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ToDisplayString(),
            IsSystem(symbol),
            symbol.Locations.FirstOrDefault(),
            inSets.ToImmutable(),
            beforeSystems.ToImmutable(),
            afterSystems.ToImmutable(),
            beforeSets.ToImmutable(),
            afterSets.ToImmutable());
    }

    private static bool IsSystem(INamedTypeSymbol symbol)
        => symbol.AllInterfaces.Any(static type =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == "global::Sia.ISystem");

    private static ImmutableArray<SystemInfo> SortAndDistinct(
        ImmutableArray<SystemInfo> infos)
    {
        var builder = ImmutableArray.CreateBuilder<SystemInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var info in infos
            .Where(static info => info.IsValid)
            .OrderBy(static info => info.Type, StringComparer.Ordinal)) {
            if (seen.Add(info.Type)) {
                builder.Add(info);
            }
        }

        return builder.ToImmutable();
    }

    private static SourceText GenerateProvider(ImmutableArray<SystemInfo> infos)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("[assembly: global::Sia.SiaSystemDescriptorProviderAttribute(typeof(global::Sia.Generated.SiaGeneratedSystemDescriptorProvider))]");
        builder.AppendLine();
        builder.AppendLine("namespace Sia.Generated;");
        builder.AppendLine();
        builder.AppendLine("internal sealed class SiaGeneratedSystemDescriptorProvider : global::Sia.ISystemDescriptorProvider");
        builder.AppendLine("{");
        builder.AppendLine("    public bool TryGet(global::System.Type systemType, out global::Sia.SystemDescriptor descriptor)");
        builder.AppendLine("    {");

        foreach (var info in infos) {
            builder.Append("        if (systemType == typeof(");
            builder.Append(info.Type);
            builder.AppendLine(")) {");
            builder.Append("            descriptor = global::Sia.SystemDescriptor.ForId(global::Sia.SystemId.ForType(typeof(");
            builder.Append(info.Type);
            builder.Append(")))");
            AppendDescriptorCalls(builder, info);
            builder.AppendLine(";");
            builder.AppendLine("            return true;");
            builder.AppendLine("        }");
            builder.AppendLine();
        }

        builder.AppendLine("        descriptor = default!;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return SourceText.From(builder.ToString(), Encoding.UTF8);
    }

    private static void AppendDescriptorCalls(StringBuilder builder, SystemInfo info)
    {
        foreach (var set in info.InSets) {
            builder.AppendLine();
            builder.Append("                .InSet<");
            builder.Append(set);
            builder.Append(">()");
        }
        foreach (var system in info.BeforeSystems) {
            builder.AppendLine();
            builder.Append("                .Before<");
            builder.Append(system);
            builder.Append(">()");
        }
        foreach (var system in info.AfterSystems) {
            builder.AppendLine();
            builder.Append("                .After<");
            builder.Append(system);
            builder.Append(">()");
        }
        foreach (var set in info.BeforeSets) {
            builder.AppendLine();
            builder.Append("                .BeforeSet<");
            builder.Append(set);
            builder.Append(">()");
        }
        foreach (var set in info.AfterSets) {
            builder.AppendLine();
            builder.Append("                .AfterSet<");
            builder.Append(set);
            builder.Append(">()");
        }
    }
}
