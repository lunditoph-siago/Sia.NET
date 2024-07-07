using CommunityToolkit.HighPerformance.Buffers;

namespace Sia.Tests.Systems;

public partial class SystemBaseTests
{
    public partial record struct VariableData([Sia] int Value);

    public partial record struct ConstData([Sia] int Value);

    public class AssertSystem(int expected) : SystemBase(
        Matchers.Of<VariableData>())
    {
        public override void Execute(World world, IEntityQuery query)
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
                Matchers.Of<VariableData>(),
                children: SystemChain.Empty.Add<AssertSystem>(() => new(2)))
        {
            public override void Execute(World world, IEntityQuery query)
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
                Matchers.Of<VariableData, ConstData>(),
                children: SystemChain.Empty.Add<AssertSystem>(() => new(2)))
        {
            public override void Execute(World world, IEntityQuery query)
            {
                query.ForSlice(static (ref VariableData c1, ref ConstData c2) => { c1.Value += c2.Value; });
                query.ForSliceOnParallel(static (ref VariableData c1, ref ConstData c2) => { c1.Value += c2.Value; });
            }
        }

        public class UpdateMultiComponentsWithTriggerSystem()
            : SystemBase(
                Matchers.Of<VariableData, ConstData>(),
                EventUnion.Of<ConstData.SetValue>())
        {
            public override void Execute(World world, IEntityQuery query)
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

        SystemChain.Empty
            .Add<SingleComponentUpdateContext.UpdateSingleComponentSystem>()
            .CreateStage(fixture.World)
            .Tick();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SystemBaseMultiComponents_Match_Test(bool padding)
    {
        using var fixture = new WorldFixture();
        fixture.Prepare(new VariableData(), new ConstData { Value = 1 }, 100, padding);

        SystemChain.Empty
            .Add<MultiComponentsUpdateContext.UpdateMultiComponentsSystem>()
            .CreateStage(fixture.World)
            .Tick();
    }

    [Fact]
    public void SystemBaseMultiComponents_Trigger_Test()
    {
        using var fixture = new WorldFixture();
        var entity = fixture.World.CreateInArrayHost(HList.Create(new VariableData(), new ConstData()));

        var stage = SystemChain.Empty
            .Add<MultiComponentsUpdateContext.UpdateMultiComponentsWithTriggerSystem>()
            .CreateStage(fixture.World);

        entity.Execute(new ConstData.SetValue(1));
        stage.Tick();

        // Assert
        Assert.Equal(2, new VariableData.View(entity).Value);
    }
}