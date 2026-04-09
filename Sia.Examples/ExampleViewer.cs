using System.Runtime.InteropServices.JavaScript;

namespace Sia_Examples;

public static partial class ExampleViewer
{
    private static readonly ExampleRunner _runner = new();

#if !BROWSER
    public static void Run()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("=== Sia.NET Examples ===\n");

            var examples = _runner.Examples;
            for (int i = 0; i < examples.Count; i++)
                Console.WriteLine($"  [{i + 1,2}] {examples[i].Name} — {examples[i].Description}");

            Console.WriteLine("\n  [ 0] Quit");
            Console.Write("\nSelect: ");

            if (!int.TryParse(Console.ReadLine(), out int choice))
                continue;

            if (choice == 0) break;

            int index = choice - 1;
            if (index < 0 || index >= examples.Count)
                continue;

            Console.Clear();
            Console.Write(_runner.RunExample(index));
            Console.WriteLine("\nPress any key to return…");
            Console.ReadKey(true);
        }
    }
#else
    [JSImport("addSidebarItem", "main.js")]
    private static partial void JsAddSidebarItem(int index, string name, string desc);

    [JSImport("setOutput", "main.js")]
    private static partial void JsSetOutput(string title, string text);

    [JSImport("setOutputLoading", "main.js")]
    private static partial void JsSetOutputLoading(string title);

    [JSImport("setActive", "main.js")]
    private static partial void JsSetActive(int index);

    [JSImport("waitForClick", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    private static partial Task<int> JsWaitForClick();

    public static void Render()
    {
        var examples = _runner.Examples;
        for (int i = 0; i < examples.Count; i++)
            JsAddSidebarItem(i, examples[i].Name, examples[i].Description);
    }

    public static async Task Run()
    {
        var examples = _runner.Examples;
        while (true)
        {
            int index = await JsWaitForClick();
            if (index < 0 || index >= examples.Count) continue;

            JsSetActive(index);
            JsSetOutputLoading(examples[index].Name);
            var result = _runner.RunExample(index);
            JsSetOutput(examples[index].Name, result);
        }
    }
#endif

    public static void Dispose() => _runner.Dispose();
}
