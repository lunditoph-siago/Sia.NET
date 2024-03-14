namespace Sia_Examples;

using System.Numerics;
using Sia;

public static class Example15_PolyList
{
    public class GenericPrinter : IGenericHandler<IPolyList>
    {
        public void Handle<T>(in T value) where T : IPolyList
        {
            Console.WriteLine(value);
        }
    }

    public static void Run(World world)
    {
        var list1 = PolyList.Cons("Hello", PolyList.Create(1));
        var list2 = PolyList.Cons(12f, PolyList.Cons(new Vector3(1, 2, 3), PolyList.Create(true)));

        list1.Concat(list2, new GenericPrinter());
        list1.Remove(1, new GenericPrinter());
        list2.Remove(TypeProxy<bool>.Default, new GenericPrinter());
    }
}