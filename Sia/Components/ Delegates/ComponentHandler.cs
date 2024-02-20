namespace Sia;

public delegate void ComponentHandler<C1>(ref C1 c1);
public delegate void ComponentHandler<C1, C2>(ref C1 c1, ref C2 c2);
public delegate void ComponentHandler<C1, C2, C3>(ref C1 c1, ref C2 c2, ref C3 c3);
public delegate void ComponentHandler<C1, C2, C3, C4>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4);
public delegate void ComponentHandler<C1, C2, C3, C4, C5>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5);
public delegate void ComponentHandler<C1, C2, C3, C4, C5, C6>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6);