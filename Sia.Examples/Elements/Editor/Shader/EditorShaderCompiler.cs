using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Sia.Spirv;
using Sia.Spirv.Compiler;
using Sia.Spirv.Compiler.Compilation;
using Sia.Spirv.Compiler.Model;
using Sia.Spirv.Compiler.Translation;
using Sia.Spirv.Compiler.Translation.Back;
using Sia.Spirv.Compiler.Translation.IR;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

// Owns reference preparation. Transformations below operate only on immutable job inputs.
internal sealed class EditorShaderCompiler(MetadataReferenceProvider references)
{
    private readonly CSharpCompilationEngine _engine = new(references);
    private static int _assemblyNumber;
    public int ReferenceVersion => references.Version;

    internal static SpirvCompilationTarget Target { get; } = new() {
        KernelAbi = SpirvKernelAbi.WebGpu,
        AllowedStages = ImmutableHashSet.Create(ShaderStage.Vertex, ShaderStage.Fragment, ShaderStage.Compute),
        AllowedWgslEnables = ImmutableHashSet<string>.Empty,
        AllowedExtensions = ImmutableHashSet<string>.Empty,
        ResourceLimits = new() {
            MaxStorageBuffersPerShaderStage = 8,
            MaxStorageBuffersInVertexStage = 8,
            MaxStorageBuffersInFragmentStage = 8,
            MaxStorageBufferBindingSize = 128 * 1024 * 1024,
            MaxUniformBuffersPerShaderStage = 12,
            MaxUniformBufferBindingSize = 65536,
        },
    };

    public async Task<ShaderCompilation> CompileAsync(
        IReadOnlyList<CSharpSourceDocument> files, CancellationToken cancellationToken, bool requireEntry = true)
    {
        var clock = Stopwatch.StartNew();
        var intrinsicImage = await references.EnsureHostReferenceAsync("Sia.Spirv.Core", cancellationToken);
        // Every action shares this program cache and the exact host types used by its Render delegate.
        var hostAssemblies = new[] {
            typeof(EditorShaderCompiler).Assembly.GetName().Name!,
            "Sia.Prelude", "Sia.Math", "Sia.Window", "Sia.Input", "Sia.RenderGraph", "Sia.Graphics", "Sia.WebGPU",
        };
        foreach (var assembly in hostAssemblies)
            await references.EnsureHostReferenceAsync(assembly, cancellationToken);
        var referenceVersion = ReferenceVersion;
        var output = await _engine.CompileAsync(new(
            $"EditorShader_{Interlocked.Increment(ref _assemblyNumber)}",
            files, SelectOutputKind: ChooseOutputKind), cancellationToken);
        var diagnostics = output.Diagnostics.ToList();
        var csharpMilliseconds = clock.Elapsed.TotalMilliseconds;
        if (!output.Success) {
            return new(null, intrinsicImage, [], diagnostics, referenceVersion, csharpMilliseconds, 0);
        }
        var image = output.AssemblyImage!;
        clock.Restart();
        var frontend = await Task.Run(() => new SpirvFrontend().Analyze(image, intrinsicImage), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        diagnostics.AddRange(frontend.Diagnostics.Select(static diagnostic => new CSharpCompilationDiagnostic(
            diagnostic.Id,
            $"IL analysis · {diagnostic.Method}" +
                (diagnostic.IlOffset is { } offset ? $" · IL_{offset:x4}" : "") + $": {diagnostic.Message}",
            diagnostic.Severity.ToString() == "Error" ? NotebookDiagnosticSeverity.Error : NotebookDiagnosticSeverity.Warning,
            null, null, 0, 0)));
        if (requireEntry && frontend.Kernels.Count == 0) {
            diagnostics.Add(new("SHADER_ENTRY", "No shader entry found. Add a static method with [SpirvFragmentShader], [SpirvVertexShader] or [SpirvKernel].",
                NotebookDiagnosticSeverity.Error, null, null, 0, 0));
        }
        var entries = frontend.Kernels.Select(static kernel => new ShaderEntry(
            kernel.QualifiedName, kernel.Name, kernel.MetadataToken, kernel.Stage, kernel.Parameters)).ToArray();
        return new(image, intrinsicImage, entries, diagnostics, referenceVersion, csharpMilliseconds, clock.Elapsed.TotalMilliseconds);
    }

    private static OutputKind ChooseOutputKind(Compilation compilation, CancellationToken cancellationToken)
    {
        var renderType = compilation.GetTypeByMetadataName("Sia.Reactive.ReactiveNode");
        var frameType = compilation.GetTypeByMetadataName("Sia_Examples.Editor.EditorPipelineFrame");
        var hooksType = compilation.GetTypeByMetadataName("Sia.Reactive.Hooks");
        var hasRender = compilation.GetSymbolsWithName("Render", SymbolFilter.Member, cancellationToken)
            .OfType<IMethodSymbol>().Any(method => method.IsStatic && !method.IsAbstract && !method.IsGenericMethod
                && method.DeclaredAccessibility == Accessibility.Public
                && IsExported(method.ContainingType)
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, renderType)
                && method.Parameters is [{ RefKind: RefKind.In } frame, { RefKind: RefKind.Ref } hooks]
                && SymbolEqualityComparer.Default.Equals(frame.Type, frameType)
                && SymbolEqualityComparer.Default.Equals(hooks.Type, hooksType));
        var hasTopLevel = compilation.SyntaxTrees.Any(tree => tree.GetRoot(cancellationToken).ChildNodes()
            .Any(node => node is Microsoft.CodeAnalysis.CSharp.Syntax.GlobalStatementSyntax));
        return hasRender && !hasTopLevel || compilation.GetEntryPoint(cancellationToken) is null
            && !compilation.GetDiagnostics(cancellationToken).Any(diagnostic => diagnostic.Id == "CS0017")
            ? OutputKind.DynamicallyLinkedLibrary : OutputKind.ConsoleApplication;
    }

    private static bool IsExported(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public || current.IsGenericType) return false;
        return true;
    }

    internal static ShaderArtifact Write(ShaderCompilation compilation, int token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kernel = compilation.Entries.Single(entry => entry.MetadataToken == token);
        var clock = Stopwatch.StartNew();
        var module = new SpirvCompiler().CompileModule(new(compilation.Image!, token, compilation.Intrinsics) { Target = Target });
        var lowering = clock.Elapsed.TotalMilliseconds;
        cancellationToken.ThrowIfCancellationRequested();
        clock.Restart();
        var wgsl = WgslWriter.Write(module, Target);
        var wgslMilliseconds = clock.Elapsed.TotalMilliseconds;
        cancellationToken.ThrowIfCancellationRequested();
        clock.Restart();
        // Browser raster coordinates already follow WebGPU; do not apply a Vulkan viewport flip.
        var spirv = SpirvWriter.Write(module, Target, new(AdjustCoordinateSpace: false));
        var spirvMilliseconds = clock.Elapsed.TotalMilliseconds;
        cancellationToken.ThrowIfCancellationRequested();
        clock.Restart();
        var translated = ShaderTranslator.SpirvToWgsl(spirv, Target);
        cancellationToken.ThrowIfCancellationRequested();
        var entryName = module.Functions.Single(function => function.Stage is not null).Name;
        var preview = DescribePreview(kernel, module, compilation.Program.ManifestModule.ResolveMethod(token)!.GetParameters());
        return new(kernel.QualifiedName, entryName, wgsl, spirv, translated, preview,
            lowering, wgslMilliseconds, spirvMilliseconds, clock.Elapsed.TotalMilliseconds);
    }

    private static ShaderPreviewLayout DescribePreview(ShaderEntry kernel, Sia.Spirv.Compiler.Translation.IR.Module module, ParameterInfo[] metadata)
    {
        ShaderPreviewLayout Unsupported(string reason) => new(null, 0, [], reason);
        if (kernel.Stage != SpirvShaderStage.Fragment) {
            return Unsupported("Canvas preview requires a fragment entry. Vertex and compute artifacts can still be inspected.");
        }
        if (kernel.Parameters.Any(parameter => parameter.Kind != SpirvKernelParameterKind.PushConstant)) {
            return Unsupported("Preview currently accepts scalar parameters only; textures, storage and explicit stage-input structs need a resource provider.");
        }
        var entry = module.Functions.Single(function => function.Stage == ShaderStage.Fragment);
        if (entry.Arguments.Any(argument => argument.Binding?.Builtin != "position")) {
            return Unsupported("The fullscreen host supplies fragment position only; user varyings need a matching vertex stage.");
        }
        if (entry.ReturnType is not ShaderType.Structure result || result.Members.Count != 1
            || result.Members[0].Binding?.Location != 0
            || result.Members[0].Type != new ShaderType.Vector(4, ShaderType.F32)) {
            return Unsupported("Preview requires one vec4<float> color output at location 0.");
        }
        var parameters = kernel.Parameters.Select((parameter, index) => new ShaderPreviewParameter(
            parameter.Name,
            parameter.ScalarType switch {
                SpirvScalarType.Float32 => "f32",
                SpirvScalarType.Int32 => "i32",
                SpirvScalarType.UInt32 => "u32",
                _ => throw new InvalidDataException("Unsupported preview scalar type: " + parameter.ScalarType),
            }, index * 4, metadata[index].GetCustomAttribute<RangeAttribute>()?.Minimum,
            metadata[index].GetCustomAttribute<RangeAttribute>()?.Maximum)).ToArray();
        if (parameters.Any(parameter => parameter.Min is { } min && parameter.Max is { } max
            && (!double.IsFinite(min) || !double.IsFinite(max) || min >= max)))
            return Unsupported("Parameter ranges must have finite, increasing bounds.");
        if (parameters.Length == 0) {
            return module.Globals.Any(global => global.Binding is not null)
                ? Unsupported("Shader declares resources that the preview cannot supply.")
                : new(null, 0, parameters, null);
        }
        // Validate the actual lowered WebGPU scalar ABI before publishing host offsets.
        var uniforms = module.Globals.Where(global => global.Binding is not null).ToArray();
        var vectors = (uint)((parameters.Length + 3) / 4);
        if (uniforms.Length != 1 || uniforms[0] is not {
            Space: AddressSpace.Uniform,
            Type: ShaderType.Structure { Members.Count: 1 } structure, Binding: { Group: 0 } binding
        }
            || structure.Members[0].Type != new ShaderType.Array(new ShaderType.Vector(4, ShaderType.U32), vectors)) {
            return Unsupported("Compiled scalar layout does not match the preview's verified WebGPU ABI.");
        }
        return new(binding.Binding, checked((int)vectors * 16), parameters, null);
    }
}

internal sealed record ShaderCompilation(byte[]? Image, ReadOnlyMemory<byte> Intrinsics,
    IReadOnlyList<ShaderEntry> Entries, IReadOnlyList<CSharpCompilationDiagnostic> Diagnostics,
    int ReferenceVersion, double CSharpMilliseconds, double AnalysisMilliseconds)
{
    private readonly Lazy<Assembly> _program = new(() => Assembly.Load(Image!));
    public Assembly Program => _program.Value;
    public bool Success => Image is not null
        && !Diagnostics.Any(diagnostic => diagnostic.Severity == NotebookDiagnosticSeverity.Error);
}

// Keep entry metadata, not the frontend's temporary control-flow graphs, in the editor cache.
internal sealed record ShaderEntry(string QualifiedName, string Name, int MetadataToken,
    SpirvShaderStage Stage, IReadOnlyList<SpirvKernelParameter> Parameters)
{
    public string SelectionKey => QualifiedName + "(" + string.Join(",", Parameters.Select(static parameter =>
        $"{parameter.Kind}:{parameter.StageIoLayout?.Name ?? parameter.PhysicalLayout?.LogicalType.Name ?? parameter.ScalarType.ToString()}")) + ")";
    public string DisplayName => Parameters.Count == 0 ? QualifiedName : QualifiedName + "(" +
        string.Join(", ", Parameters.Select(static parameter => $"{parameter.ScalarType} {parameter.Name}")) + ")";
}

internal sealed record ShaderPreviewParameter(string Name, string Type, int Offset, double? Min, double? Max);
internal sealed record ShaderPreviewLayout(uint? Binding, int BufferSize,
    IReadOnlyList<ShaderPreviewParameter> Parameters, string? UnsupportedReason);
internal sealed record ShaderArtifact(string QualifiedName, string EntryName, string Wgsl, byte[] Spirv,
    string TranslatedWgsl, ShaderPreviewLayout Preview, double LoweringMilliseconds,
    double WgslMilliseconds, double SpirvMilliseconds, double TranslationMilliseconds);
