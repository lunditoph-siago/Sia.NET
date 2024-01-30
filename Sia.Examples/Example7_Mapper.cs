namespace Sia_Examples;

using Sia;

public static partial class Example7_Mapper
{
    public readonly record struct ObjectId(Guid Value)
    {
        public static ObjectId Create()
            => new(Guid.NewGuid());
    }

    public static void Run(World world)
    {
        var mapper = world.AcquireAddon<Mapper<ObjectId>>();

        var id1 = ObjectId.Create();
        var id2 = ObjectId.Create();

        var e1 = world.CreateInBucketHost(Bundle.Create(
            Sid.From(id1)
        ));
        var e2 = world.CreateInBucketHost(Bundle.Create(
            Sid.From(id2)
        ));

        Console.WriteLine(mapper[id1] == e1);
        Console.WriteLine(mapper[id2] == e2);

        var id3 = ObjectId.Create();
        e2.SetSid(id3);

        Console.WriteLine(mapper[id3] == e2);
    }
}