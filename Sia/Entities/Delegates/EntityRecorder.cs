namespace Sia;

public delegate void EntityRecorder<TResult>(Entity entity, out TResult result);
public delegate void EntityRecorder<TData, TResult>(in TData data, Entity entity, out TResult result);