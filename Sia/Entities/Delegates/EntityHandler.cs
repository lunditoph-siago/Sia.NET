namespace Sia;

public delegate void EntityHandler(in EntityRef entity);
public delegate void EntityHandler<TData>(in TData data, in EntityRef entity);

public delegate void SimpleEntityHandler(EntityRef entity);
public delegate void SimpleEntityHandler<TData>(TData data, EntityRef entity);