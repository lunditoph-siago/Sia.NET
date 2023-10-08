namespace Sia;

public delegate void BufferIndexHandler(int index);
public delegate void BufferIndexHandler<TData>(in TData data, int index);