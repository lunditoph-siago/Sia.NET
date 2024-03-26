using System.Runtime.InteropServices;

namespace Sia.Samples;

public static class Program
{
    private const string DllName = "Sia.Samples.Native";

    [DllImport(DllName, EntryPoint = "run")]
    public static extern void Run();

    [STAThread]
    private static void Main() => Run();
}