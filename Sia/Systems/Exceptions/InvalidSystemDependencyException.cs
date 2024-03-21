namespace Sia;

[Serializable]
public class InvalidSystemDependencyException : Exception
{
    public InvalidSystemDependencyException() {}
    public InvalidSystemDependencyException(string message) : base(message) {}
    public InvalidSystemDependencyException(string message, Exception inner) : base(message, inner) {}
}