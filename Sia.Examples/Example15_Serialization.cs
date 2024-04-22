namespace Sia_Examples;

using System.Buffers;
using System.Text;
using MemoryPack.Compression;
using Sia;
using Sia.Serialization.Binary;

public static class Example15_Serialization
{
    public static void Run(World world)
    {
        var compressor = new BrotliCompressor();

        world.CreateInArrayHost(HList.Create(0, "asdf"));
        world.CreateInSparseHost(HList.Create(0, "asdf", 1324f, Math.PI));
        world.CreateInUnversionedHashHost(HList.Create(0, "asdf", 1324f, Math.PI));

        BinaryWorldSerializer.Serialize(ref compressor, world);

        var seq = new ReadOnlySequence<byte>(compressor.ToArray());
        var compressedLength = seq.Length;
        Console.WriteLine("Compressed:");
        Console.WriteLine(Encoding.Unicode.GetString(seq));

        var decompressor = new BrotliDecompressor();
        seq = decompressor.Decompress(seq);
        var decompressedLength = seq.Length;

        Console.WriteLine();
        Console.WriteLine("Decompressed:");
        Console.WriteLine(Encoding.Unicode.GetString(seq));

        Console.WriteLine();
        Console.WriteLine("Compression rate: " + (float)compressedLength / decompressedLength);

        var newWorld = BinaryWorldSerializer.Deserialize(ref seq);

        Console.WriteLine();
        foreach (var e in newWorld) {
            Console.WriteLine(e.Boxed);
        }
    }
}