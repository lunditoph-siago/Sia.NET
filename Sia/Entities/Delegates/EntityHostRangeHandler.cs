namespace Sia;

public delegate void EntityHostRangeHandler(IEntityHost host, int from, int to);
public delegate void EntityHostRangeHandler<TData>(IEntityHost host, in TData data, int from, int to);