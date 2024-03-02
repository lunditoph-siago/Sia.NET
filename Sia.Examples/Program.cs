using Sia;
using Sia_Examples;

void Invoke(Action<World> action)
{
    Console.WriteLine("== " + action.Method.DeclaringType + " ==");
    var world = new World();
    Context<World>.Current = world;
    action(world);
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
Invoke(Example8_Sum.Run);
Invoke(Example10_DuplicateSystem.Run);
Invoke(Example11_RPG.Run);
Invoke(Example12_EntityBuilder.Run);
Invoke(Example13_Addon.Run);
Invoke(Example14_Parallel.Run);