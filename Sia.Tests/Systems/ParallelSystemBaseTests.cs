using CommunityToolkit.HighPerformance.Buffers;

namespace Sia.Tests.Systems;

public class ParallelSystemBaseTests
{
    public partial record struct VariableData([Sia] int Value);

    public class UpdateSingleComponentSystem : ParallelSystemBase<VariableData>
    {
        protected override void HandleSlice(ref VariableData c) => c.Value++;
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParallelSystemBaseSingleComponent_Test(bool padding)
    {
        using var fixture = new WorldFixture();
        fixture.Prepare(new VariableData(), 100, padding);

        var stage = SystemChain.Empty
            .Add<UpdateSingleComponentSystem>()
            .Add<AssertSystem>(() => new (1))
            .CreateStage(fixture.World);

        stage.Tick();
    }
}