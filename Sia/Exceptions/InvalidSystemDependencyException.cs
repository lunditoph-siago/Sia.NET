namespace Sia;

[System.Serializable]
public class InvalidSystemDependencyException : System.Exception
{
    public InvalidSystemDependencyException() { }
    public InvalidSystemDependencyException(string message) : base(message) { }
    public InvalidSystemDependencyException(string message, System.Exception inner) : base(message, inner) { }
    protected InvalidSystemDependencyException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}