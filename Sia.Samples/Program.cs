using System.Runtime.InteropServices;

namespace Sia.Samples;

public static class Program
{
    [DllImport("Sia.Samples.Native", EntryPoint = "sum_array")]
    public static extern int SumArray(int[] array, ulong length);

    private static void Main(string[] args)
    {
        var largeArray = new int[100000];

        for (var i = 0; i < largeArray.Length; i++)
        {
            largeArray[i] = i;
        }

        Console.WriteLine("Calling Rust from C#: ");

        long sum = SumArray(largeArray, (ulong)largeArray.Length);

        Console.WriteLine(sum == -1 ? "Overflow error in Rust." : $"Sum of array elements: {sum}");
    }
}