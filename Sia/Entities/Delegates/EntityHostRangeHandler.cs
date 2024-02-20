namespace Sia;

public delegate void EntityHostRangeHandler(IEntityHost host, int From, int To);
public delegate void EntityHostRangeHandler<TData>(IEntityHost host, in TData data, int From, int To);