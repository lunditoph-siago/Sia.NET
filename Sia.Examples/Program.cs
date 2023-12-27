using Sia;
using Sia.Examples;

void Invoke(Action<World> action)
{
    Console.WriteLine("== " + action.Method.DeclaringType + " ==");
    var world = new World();
    world.Start(() => action(world));
    world.Dispose();
    Console.WriteLine();
}

Tests.Run();
Invoke(Example1_HealthDamage.Run);
Invoke(Example2_HealthRecover.Run);
Invoke(Example3_MoveRotator.Run);
Invoke(Example4_Aggregator.Run);
Invoke(Example5_ComponentBundle.Run);
Invoke(Example6_Hierarchy.Run);
Invoke(Example7_Mapper.Run);
Invoke(Example8_SIMD.Run);
Invoke(Example10_DuplicateSystem.Run);
Invoke(Example11_RPG.Run);