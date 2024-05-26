namespace Sia;

public delegate void ComponentRecorder<C1, TResult>(ref C1 c1, out TResult result);
public delegate void ComponentRecorder<C1, C2, TResult>(ref C1 c1, ref C2 c2, out TResult result);
public delegate void ComponentRecorder<C1, C2, C3, TResult>(ref C1 c1, ref C2 c2, ref C3 c3, out TResult result);
public delegate void ComponentRecorder<C1, C2, C3, C4, TResult>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, out TResult result);
public delegate void ComponentRecorder<C1, C2, C3, C4, C5, TResult>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, out TResult result);
public delegate void ComponentRecorder<C1, C2, C3, C4, C5, C6, TResult>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6, out TResult result);

public delegate void DataComponentRecorder<TData, C1, TResult>(in TData data, ref C1 c1, out TResult result);
public delegate void DataComponentRecorder<TData, C1, C2, TResult>(in TData data, ref C1 c1, ref C2 c2, out TResult result);
public delegate void DataComponentRecorder<TData, C1, C2, C3, TResult>(in TData data, ref C1 c1, ref C2 c2, ref C3 c3, out TResult result);
public delegate void DataComponentRecorder<TData, C1, C2, C3, C4, TResult>(in TData data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, out TResult result);
public delegate void DataComponentRecorder<TData, C1, C2, C3, C4, C5, TResult>(in TData data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, out TResult result);
public delegate void DataComponentRecorder<TData, C1, C2, C3, C4, C5, C6, TResult>(in TData data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6, out TResult result);

public delegate void ComponentRecorderWithEntity<C1, TResult>(Entity entity, ref C1 c1, out TResult result);
public delegate void ComponentRecorderWithEntity<C1, C2, TResult>(Entity entity, ref C1 c1, ref C2 c2, out TResult result);
public delegate void ComponentRecorderWithEntity<C1, C2, C3, TResult>(Entity entity, ref C1 c1, ref C2 c2, ref C3 c3, out TResult result);
public delegate void ComponentRecorderWithEntity<C1, C2, C3, C4, TResult>(Entity entity, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, out TResult result);
public delegate void ComponentRecorderWithEntity<C1, C2, C3, C4, C5, TResult>(Entity entity, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, out TResult result);
public delegate void ComponentRecorderWithEntity<C1, C2, C3, C4, C5, C6, TResult>(Entity entity, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6, out TResult result);

public delegate void DataComponentRecorderWithEntity<TData, C1, TResult>(Entity entity, in TData data, ref C1 c1, out TResult result);
public delegate void DataComponentRecorderWithEntity<TData, C1, C2, TResult>(Entity entity, in TData data, ref C1 c1, ref C2 c2, out TResult result);
public delegate void DataComponentRecorderWithEntity<TData, C1, C2, C3, TResult>(Entity entity, in TData data, ref C1 c1, ref C2 c2, ref C3 c3, out TResult result);
public delegate void DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, TResult>(Entity entity, in TData data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, out TResult result);
public delegate void DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, TResult>(Entity entity, in TData data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, out TResult result);
public delegate void DataComponentRecorderWithEntity<TData, C1, C2, C3, C4, C5, C6, TResult>(Entity entity, in TData data, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6, out TResult result);