using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Sia.Examples.Notebook;

internal static class ManagedAssemblyExecutor
{
    private static readonly SemaphoreSlim s_ExecutionGate = new(1, 1);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Loads a freshly emitted in-memory assembly, not application code trimming can affect.")]
    public static async Task<NotebookExecuteResult> ExecuteAsync(byte[] assemblyImage)
    {
        await s_ExecutionGate.WaitAsync();
        try {
            var assembly = Assembly.Load(assemblyImage);
            DynamicAssemblyRegistry.Register(assembly.GetName().Name!, assembly);
            using var console = new ProgramConsole(int.MaxValue);
            return await ExecuteAsync(assembly, console);
        }
        catch (Exception error) { return new(false, "", error.ToString()); }
        finally { s_ExecutionGate.Release(); }
    }

    internal static async Task<NotebookExecuteResult> ExecuteAsync(Assembly assembly, ProgramConsole console)
    {
        using var capture = console.Capture();
        var success = true;
        try {
            var entry = assembly.EntryPoint
                ?? throw new InvalidOperationException("No entry point found in the compiled program.");
            if (entry.Name == "<Main>") {
                var asyncEntry = entry.DeclaringType!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .SingleOrDefault(method => method.Name is "Main" or "<Main>$"
                        && typeof(Task).IsAssignableFrom(method.ReturnType)
                        && method.GetParameters().Select(parameter => parameter.ParameterType)
                            .SequenceEqual(entry.GetParameters().Select(parameter => parameter.ParameterType)));
                entry = asyncEntry ?? entry;
            }
            var result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [Array.Empty<string>()]);
            if (result is Task task) await task;
        }
        catch (Exception error) {
            var inner = error is TargetInvocationException { InnerException: { } captured } ? captured : error;
            System.Console.Error.WriteLine(inner);
            success = false;
        }
        var snapshot = console.Snapshot();
        return new(success, snapshot.Output, snapshot.Error);
    }
}
