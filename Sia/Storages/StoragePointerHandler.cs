namespace Sia;

public delegate void StoragePointerHandler(nint pointer, int version);
public delegate void StoragePointerHandler<TData>(in TData data, nint pointer, int version);