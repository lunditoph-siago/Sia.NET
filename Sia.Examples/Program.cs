using Sia.Examples.Runtime;

var examples = new ExampleItem[]
{
    new("Health & Damage", "Demonstrates health and damage system", Sia_Examples.Example1_HealthDamage.Run),
    new("Health Recovery", "Demonstrates automatic health recovery system", Sia_Examples.Example2_HealthRecover.Run),
    new("Move & Rotate", "Demonstrates movement and rotation system", Sia_Examples.Example3_MoveRotator.Run),
    new("Aggregator", "Demonstrates aggregator system", Sia_Examples.Example4_Aggregator.Run),
    new("Component Bundle", "Demonstrates component bundles", Sia_Examples.Example5_ComponentBundle.Run),
    new("Hierarchy", "Demonstrates hierarchy system", Sia_Examples.Example6_Hierarchy.Run),
    new("Mapper", "Demonstrates mapper functionality", Sia_Examples.Example7_Mapper.Run),
    new("SIMD", "Demonstrates SIMD operations", Sia_Examples.Example8_Sum.Run),
    new("Duplicate System", "Demonstrates duplicate system", Sia_Examples.Example10_DuplicateSystem.Run),
    new("RPG System", "Demonstrates RPG system", Sia_Examples.Example11_RPG.Run),
    new("Addon", "Demonstrates addon system", Sia_Examples.Example12_Addon.Run),
    new("Parallel", "Demonstrates parallel processing", Sia_Examples.Example13_Parallel.Run),
    new("Runner Context", "Demonstrates runner context", Sia_Examples.Example14_RunnerWithContext.Run),
    new("Serialization", "Demonstrates serialization", Sia_Examples.Example15_Serialization.Run),
    new("Event System", "Demonstrates event system", Sia_Examples.Example16_EventSystem.Run)
};

var viewer = new ExampleViewer(examples);
viewer.Run();