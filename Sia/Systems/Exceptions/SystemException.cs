namespace Sia;

[Serializable]
public class SystemException : System.Exception
{
    public SystemException() { }
    public SystemException(string message) : base(message) { }
    public SystemException(string message, Exception inner) : base(message, inner) { }
}