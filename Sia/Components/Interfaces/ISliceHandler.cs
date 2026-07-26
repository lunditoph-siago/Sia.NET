namespace Sia;

public interface ISliceHandler<C1>
{
    void Handle(ref C1 c1);
}

public interface ISliceHandler<C1, C2>
{
    void Handle(ref C1 c1, ref C2 c2);
}

public interface ISliceHandler<C1, C2, C3>
{
    void Handle(ref C1 c1, ref C2 c2, ref C3 c3);
}

public interface ISliceHandler<C1, C2, C3, C4>
{
    void Handle(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4);
}

public interface ISliceHandler<C1, C2, C3, C4, C5>
{
    void Handle(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5);
}

public interface ISliceHandler<C1, C2, C3, C4, C5, C6>
{
    void Handle(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6);
}
