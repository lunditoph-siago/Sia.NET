namespace Sia.Tests.Entities;

public sealed class ManagedSliceCompatibilityTests
{
    public struct Managed { public string? Text; }

    public sealed class CountingHost<T> : ArrayEntityHost<T>, IEntityHost<T> where T : struct, IHList
    {
        public int Reads;

        EntityDescriptor IEntityHost.Descriptor
        {
            get
            {
                if (++Reads > 1) throw new InvalidOperationException("descriptor read twice");
                return Descriptor;
            }
        }
    }

    private sealed class SingleQuery(IEntityHost host) : IEntityQuery
    {
        public int Version => 0;
        public IReadOnlyList<IEntityHost> Hosts { get; } = [host];
        public void Dispose() { }
    }

    private struct Handler :
        ISliceHandler<Managed>,
        ISliceHandler<Managed, Managed>,
        ISliceHandler<Managed, Managed, Managed>,
        ISliceHandler<Managed, Managed, Managed, Managed>,
        ISliceHandler<Managed, Managed, Managed, Managed, Managed>,
        ISliceHandler<Managed, Managed, Managed, Managed, Managed, Managed>
    {
        public int Count;
        public void Handle(ref Managed a) => Count++;
        public void Handle(ref Managed a, ref Managed b) => Count++;
        public void Handle(ref Managed a, ref Managed b, ref Managed c) => Count++;
        public void Handle(ref Managed a, ref Managed b, ref Managed c, ref Managed d) => Count++;
        public void Handle(ref Managed a, ref Managed b, ref Managed c, ref Managed d, ref Managed e) => Count++;
        public void Handle(ref Managed a, ref Managed b, ref Managed c, ref Managed d, ref Managed e, ref Managed f) => Count++;
    }

    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void Struct_handlers_read_custom_host_descriptor_once(int count)
    {
        using var host = new CountingHost<HList<Managed, EmptyHList>>();
        for (var i = 0; i < count; i++) host.Create(HList.From(new Managed { Text = "kept" }));
        using var query = new SingleQuery(host);
        for (var arity = 1; arity <= 6; arity++) {
            host.Reads = 0;
            var handler = new Handler();
            switch (arity) {
                case 1: query.ForSlice<Managed, Handler>(ref handler); break;
                case 2: query.ForSlice<Managed, Managed, Handler>(ref handler); break;
                case 3: query.ForSlice<Managed, Managed, Managed, Handler>(ref handler); break;
                case 4: query.ForSlice<Managed, Managed, Managed, Managed, Handler>(ref handler); break;
                case 5: query.ForSlice<Managed, Managed, Managed, Managed, Managed, Handler>(ref handler); break;
                case 6: query.ForSlice<Managed, Managed, Managed, Managed, Managed, Managed, Handler>(ref handler); break;
            }
            Assert.Equal(count, handler.Count);
            Assert.Equal(1, host.Reads);
        }
    }

    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void Delegate_handlers_read_custom_host_descriptor_once(int count)
    {
        using var host = new CountingHost<HList<Managed, EmptyHList>>();
        for (var i = 0; i < count; i++) host.Create(HList.From(new Managed { Text = "kept" }));
        using var query = new SingleQuery(host);
        for (var arity = 1; arity <= 6; arity++) {
            host.Reads = 0;
            var calls = 0;
            switch (arity) {
                case 1: query.ForSlice<Managed>((ref Managed a) => calls++); break;
                case 2: query.ForSlice<Managed, Managed>((ref Managed a, ref Managed b) => calls++); break;
                case 3: query.ForSlice<Managed, Managed, Managed>((ref Managed a, ref Managed b, ref Managed c) => calls++); break;
                case 4: query.ForSlice<Managed, Managed, Managed, Managed>((ref Managed a, ref Managed b, ref Managed c, ref Managed d) => calls++); break;
                case 5: query.ForSlice<Managed, Managed, Managed, Managed, Managed>((ref Managed a, ref Managed b, ref Managed c, ref Managed d, ref Managed e) => calls++); break;
                case 6: query.ForSlice<Managed, Managed, Managed, Managed, Managed, Managed>((ref Managed a, ref Managed b, ref Managed c, ref Managed d, ref Managed e, ref Managed f) => calls++); break;
            }
            Assert.Equal(count, calls);
            Assert.Equal(1, host.Reads);
        }
    }
}
