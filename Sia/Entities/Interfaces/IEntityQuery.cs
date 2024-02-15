namespace Sia;

public interface IEntityQuery : IDisposable
{
    int Count {
        get {
            int count = 0;
            for (int i = 0; i != Hosts.Count; ++i) {
                count += Hosts[i].Count;
            }
            return count;
        }
    }

    IReadOnlyList<IEntityHost> Hosts { get; }
}