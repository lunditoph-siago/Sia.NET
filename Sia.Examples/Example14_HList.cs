namespace Sia_Examples;

using System.Numerics;
using Sia;

public static class Example14_HList
{
    public class GenericPrinter : IGenericHandler<IHList>
    {
        public void Handle<T>(in T value) where T : IHList
        {
            Console.WriteLine(value);
        }
    }

    public static void Run(World world)
    {
        var list1 = HList.Cons("Hello", HList.Create(1));
        var list2 = HList.Cons(12f, HList.Cons(new Vector3(1, 2, 3), HList.Create(true)));

        list1.Concat(list2, new GenericPrinter());
        list1.Remove(1, new GenericPrinter());
        list2.Remove(TypeProxy<bool>.Default, new GenericPrinter());
    }
}