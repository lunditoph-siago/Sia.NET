namespace Sia;

public delegate void EntityRecorder<TResult>(in EntityRef entity, ref TResult result);
public delegate void EntityRecorder<TData, TResult>(in TData data, in EntityRef entity, ref TResult result);