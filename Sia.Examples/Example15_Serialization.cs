namespace Sia_Examples;

using System.Buffers;
using System.Text;
using MemoryPack;
using MemoryPack.Compression;
using Sia;
using Sia.Serialization.Binary;

[MemoryPackable]
public partial record struct C1(string Value);

public class Likes<T> : IRelation;

[MemoryPackable]
public partial record struct Has(int Count) : IRelation;

public static class Example15_Serialization
{
    public static void Run(World world)
    {
        var compressor = new BrotliCompressor();

        var e1 = world.CreateInArrayHost(HList.Create(new C1("?"), 0, "asdf"));
        var e2 = world.CreateInSparseHost(HList.Create(0, "asdf", 1324f, Math.PI));
        e2.AddRelation<Likes<float>>(e2);
        e2.AddRelation(new Has(31415), e1);
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

        var newWorld = new World();
        BinaryWorldSerializer.Deserialize(ref seq, newWorld);

        Console.WriteLine();
        foreach (var e in newWorld) {
            Console.WriteLine(e.Boxed);
        }
    }
}