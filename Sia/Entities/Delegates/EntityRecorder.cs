namespace Sia;

public delegate void EntityRecorder<TResult>(in EntityRef entity, out TResult result);
public delegate void EntityRecorder<TData, TResult>(in EntityRef entity, in TData data, out TResult result);