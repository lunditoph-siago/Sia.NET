using System.IO.Compression;
using Sia_Examples.Notebook;

namespace Sia_BrowserTest.Acceptance;

public sealed class NotebookLogicAcceptance : IAcceptanceStage
{
    public string Name => "3. Notebook logic";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync("document parser preserves structured blocks", TestParserAsync);
        await context.CaseAsync("program builder maps cells and output", TestProgramBuilderAsync);
        await context.CaseAsync("assembly resolver maps namespaces", TestAssemblyResolverAsync);
        await context.CaseAsync("NuGet extractor selects the best TFM", TestNuGetExtractorAsync);
        await context.CaseAsync("package registry publishes immutable states", TestPackageRegistryAsync);
    }

    private static Task TestParserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string xml = """
            <Notebook Title="Acceptance">
              <Packages>
                <Package Source="Framework" Id="System.Runtime" />
              </Packages>
              <Section Title="Core">
                <Paragraph>Hello <Code>World</Code></Paragraph>
                <List><Item>One</Item><Item>Two</Item></List>
                <CodeCell Id="cell" Editable="true">
                  using System;
                    Console.WriteLine("ok");
                </CodeCell>
              </Section>
            </Notebook>
            """;

        var document = NotebookDocumentParser.Parse(xml);
        AcceptanceAssert.Equal("Acceptance", document.Title);
        AcceptanceAssert.Equal(1, document.Packages.Count);
        AcceptanceAssert.Equal(3, document.Sections[0].Blocks.Count);
        var paragraph = (ParagraphBlock)document.Sections[0].Blocks[0];
        AcceptanceAssert.Equal(2, paragraph.Inlines.Count);
        var cell = (CodeCellBlock)document.Sections[0].Blocks[2];
        AcceptanceAssert.Equal(
            "using System;\n  Console.WriteLine(\"ok\");",
            cell.InitialSource);
        return Task.CompletedTask;
    }

    private static Task TestProgramBuilderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var program = NotebookProgramBuilder.Build([
            ("first", "Console.WriteLine(\"first\");"),
            ("second", "Console.WriteLine(\"second\");\npublic sealed class Marker { }"),
        ]);

        AcceptanceAssert.True(program.NeedsWrapperUsing, "Type declarations were not wrapped.");
        AcceptanceAssert.Equal(
            "first",
            program.ResolveCellId(program.CellRanges[0].StatementsStartLine));
        AcceptanceAssert.Equal(
            "second",
            program.ResolveCellId(program.CellRanges[1].TypesStartLine!.Value));

        var second = program.CellRanges[1];
        var output = NotebookProgramBuilder.SliceOutput(
            second.StartToken + "accepted" + second.EndToken,
            program);
        AcceptanceAssert.Equal("accepted", output["second"]);
        return Task.CompletedTask;
    }

    private static Task TestAssemblyResolverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var namespaces = AssemblyReferenceResolver.ResolveNamespaces("""
            using System.Collections.Immutable;
            using Sia;
            """);
        var assemblies = AssemblyReferenceResolver.ResolveAssemblyNames(
            namespaces,
            ["System.Runtime", "System.Collections.Immutable", "Sia"]);
        AcceptanceAssert.SequenceEqual(
            ["Sia", "System.Collections.Immutable"],
            assemblies.Order(StringComparer.Ordinal));
        return Task.CompletedTask;
    }

    private static Task TestNuGetExtractorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true)) {
            WriteEntry(archive, "lib/netstandard2.0/Probe.dll", [1]);
            WriteEntry(archive, "lib/net8.0/Probe.dll", [8]);
        }
        buffer.Position = 0;
        using var readable = new ZipArchive(buffer, ZipArchiveMode.Read);
        var assemblies = NuGetAssemblyExtractor.Extract(readable, "Probe", "1.0.0");
        AcceptanceAssert.Equal(1, assemblies.Count);
        AcceptanceAssert.Equal("Probe", assemblies[0].Name);
        AcceptanceAssert.SequenceEqual<byte>([8], assemblies[0].Image);
        return Task.CompletedTask;
    }

    private static Task TestPackageRegistryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registry = new PackageRegistry();
        var package = new PackageRef(PackageSource.Framework, "System.Runtime", null);
        AcceptanceAssert.True(registry.Declare(package), "Initial declaration was ignored.");
        AcceptanceAssert.False(registry.Declare(package), "Duplicate declaration was accepted.");
        AcceptanceAssert.True(
            registry.Resolve(new(package, PackageLoadState.Loaded, null)),
            "Loaded state was ignored.");
        AcceptanceAssert.Equal(PackageLoadState.Loaded, registry.Snapshot[0].State);
        return Task.CompletedTask;
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
