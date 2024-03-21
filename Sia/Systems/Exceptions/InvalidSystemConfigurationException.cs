namespace Sia;

[Serializable]
public class InvalidSystemConfigurationException : Exception
{
    public InvalidSystemConfigurationException() {}
    public InvalidSystemConfigurationException(string message) : base(message) {}
    public InvalidSystemConfigurationException(string message, Exception inner) : base(message, inner) {}
}