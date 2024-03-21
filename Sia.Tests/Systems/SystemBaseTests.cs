using CommunityToolkit.HighPerformance.Buffers;

namespace Sia.Tests.Systems;

public partial class SystemBaseTests
{
    public partial record struct VariableData([Sia] int Value);

    public partial record struct ConstData([Sia] int Value);

    public partial record struct Padding1;

    public partial record struct Padding2;

    public partial record struct Padding3;

    public partial record struct Padding4;

    public class AssertSystem(int expected)
        : SystemBase(matcher: Matchers.Of<VariableData>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            using var mem = SpanOwner<int>.Allocate(query.Count);

            query.RecordSlices(mem.DangerousGetArray(),
                static (ref VariableData num, out int value) => { value = num.Value; });

            Assert.All(mem.DangerousGetArray(), data => Assert.Equal(expected, data));
        }
    }

    public static class SingleComponentUpdateContext
    {
        public class UpdateSingleComponentSystem()
            : SystemBase(
                children: SystemChain.Empty.Add<AssertSystem>(() => new(2)),
                matcher: Matchers.Of<VariableData>())
        {
            public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
            {
                query.ForSlice(static (ref VariableData c) => { c.Value++; });
                query.ForSliceOnParallel(static (ref VariableData c) => { c.Value++; });
            }
        }
    }

    public static class MultiComponentsUpdateContext
    {
        public class UpdateMultiComponentsSystem()
            : SystemBase(
                children: SystemChain.Empty.Add<AssertSystem>(() => new(2)),
                matcher: Matchers.Of<VariableData, ConstData>())
        {
            public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
            {
                query.ForSlice(static (ref VariableData c1, ref ConstData c2) => { c1.Value += c2.Value; });
                query.ForSliceOnParallel(static (ref VariableData c1, ref ConstData c2) => { c1.Value += c2.Value; });
            }
        }

        public class UpdateMultiComponentsWithTriggerSystem()
            : SystemBase(
                matcher: Matchers.Of<VariableData, ConstData>(),
                trigger: EventUnion.Of<ConstData.SetValue>())
        {
            public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
            {
                query.ForSlice(static (ref VariableData c1, ref ConstData c2) => { c1.Value += c2.Value; });
                query.ForSliceOnParallel(static (ref VariableData c1, ref ConstData c2) => { c1.Value += c2.Value; });
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SystemBaseSingleComponent_Match_Test(bool padding)
    {
        using var fixture = new WorldFixture();
        fixture.Prepare(new VariableData(), 100, padding);

        var scheduler = new Scheduler();

        fixture.World.RegisterSystem<SingleComponentUpdateContext.UpdateSingleComponentSystem>(scheduler);

        scheduler.Tick();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SystemBaseMultiComponents_Match_Test(bool padding)
    {
        using var fixture = new WorldFixture();
        fixture.Prepare(new VariableData(), new ConstData { Value = 1 }, 100, padding);

        var scheduler = new Scheduler();

        fixture.World.RegisterSystem<MultiComponentsUpdateContext.UpdateMultiComponentsSystem>(scheduler);

        scheduler.Tick();
    }

    [Fact]
    public void SystemBaseMultiComponents_Trigger_Test()
    {
        using var fixture = new WorldFixture();

        var component = fixture.World.CreateInArrayHost(HList.Create(new VariableData(), new ConstData()));

        var scheduler = new Scheduler();

        fixture.World.RegisterSystem<MultiComponentsUpdateContext.UpdateMultiComponentsWithTriggerSystem>(scheduler);

        ref var variable = ref component.Get<VariableData>();

        _ = new ConstData.View(component) {
            Value = 1
        };

        scheduler.Tick();

        // Assert
        Assert.Equal(1, variable.Value);
    }
}