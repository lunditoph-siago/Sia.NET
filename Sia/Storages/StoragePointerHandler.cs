namespace Sia;

public delegate void StoragePointerHandler(long pointer);
public delegate void StoragePointerHandler<TData>(in TData data, long pointer);