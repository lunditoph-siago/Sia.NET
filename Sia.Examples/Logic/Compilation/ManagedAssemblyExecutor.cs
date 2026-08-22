using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Sia_Examples.Notebook;

internal static class ManagedAssemblyExecutor
{
    private static readonly SemaphoreSlim _executionGate = new(1, 1);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Loads a freshly emitted in-memory assembly, not application code trimming can affect.")]
    public static async Task<NotebookExecuteResult> ExecuteAsync(byte[] assemblyImage)
    {
        await _executionGate.WaitAsync();
        var stdOut = new StringWriter();
        var stdErr = new StringWriter();
        var originalOut = global::System.Console.Out;
        var originalErr = global::System.Console.Error;
        global::System.Console.SetOut(stdOut);
        global::System.Console.SetError(stdErr);
        try {
            var assembly = Assembly.Load(assemblyImage);
            DynamicAssemblyRegistry.Register(assembly.GetName().Name!, assembly);
            var entryPoint = assembly.EntryPoint
                ?? throw new InvalidOperationException("No entry point found in the compiled program.");
            var result = entryPoint.Invoke(null, [Array.Empty<string>()]);
            if (result is Task task) {
                await task;
            }
            return new(true, stdOut.ToString(), stdErr.ToString());
        }
        catch (Exception e) {
            var inner = e is TargetInvocationException { InnerException: { } captured } ? captured : e;
            stdErr.WriteLine(inner.ToString());
            return new(false, stdOut.ToString(), stdErr.ToString());
        }
        finally {
            global::System.Console.SetOut(originalOut);
            global::System.Console.SetError(originalErr);
            stdOut.Dispose();
            stdErr.Dispose();
            _executionGate.Release();
        }
    }
}
