namespace Sia;

public interface IJob
{
    void Throw(Exception e);
    void Invoke();
}