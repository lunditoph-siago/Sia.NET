namespace Sia;

[Serializable]
public class InvalidSystemConfigurationException : Exception
{
    public InvalidSystemConfigurationException() { }
    public InvalidSystemConfigurationException(string message) : base(message) { }
    public InvalidSystemConfigurationException(string message, Exception inner) : base(message, inner) { }
    protected InvalidSystemConfigurationException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}