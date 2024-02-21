namespace Sia;

public delegate void InAction<TData>(in TData data);
public delegate void GroupAction((int, int) range);
public delegate void GroupAction<TData>(in TData data, (int, int) range);