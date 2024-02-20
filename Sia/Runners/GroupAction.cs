namespace Sia;

public delegate void GroupAction((int, int) range);
public delegate void GroupAction<TData>(TData data, (int, int) range);