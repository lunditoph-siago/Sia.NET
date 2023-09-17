namespace Sia;

public delegate void WorldEntityHandler(in EntityRef entity);
public delegate void WorldEntityHandler<TData>(in TData data, in EntityRef entity);