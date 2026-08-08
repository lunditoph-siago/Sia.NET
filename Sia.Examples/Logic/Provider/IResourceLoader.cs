namespace Sia_Examples.Notebook;

public interface IResourceLoader
{
    public ValueTask<byte[]> FetchBytesAsync(
        string url,
        CancellationToken cancellationToken = default);

    public ValueTask<string> FetchTextAsync(
        string url,
        CancellationToken cancellationToken = default);
}
