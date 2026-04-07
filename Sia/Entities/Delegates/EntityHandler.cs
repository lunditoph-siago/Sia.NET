namespace Sia;

public delegate void EntityHandler(Entity entity);
public delegate void EntityHandler<TData>(in TData data, Entity entity);

public delegate void SimpleEntityHandler(Entity entity);
public delegate void SimpleEntityHandler<in TData>(TData data, Entity entity);