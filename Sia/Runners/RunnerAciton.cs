namespace Sia;

public delegate void RunnerAction<TData>(TData data, (int, int) range);