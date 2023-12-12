namespace Sia;

using System.Diagnostics.CodeAnalysis;

public interface IBuffer<T> : IDisposable
{
    int Capacity { get; }
    ref T GetRef(int index);
}