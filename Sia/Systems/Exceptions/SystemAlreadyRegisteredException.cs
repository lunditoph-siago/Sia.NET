namespace Sia;

[Serializable]
public class SystemAlreadyRegisteredException : Exception
{
    public SystemAlreadyRegisteredException() {}
    public SystemAlreadyRegisteredException(string message) : base(message) {}
    public SystemAlreadyRegisteredException(string message, Exception inner) : base(message, inner) {}
}