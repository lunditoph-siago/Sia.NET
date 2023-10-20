using Sia.Examples;

void Invoke(Action action)
{
    Console.WriteLine("== " + action.Method.DeclaringType + " ==");
    action();
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