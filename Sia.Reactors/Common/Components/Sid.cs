namespace Sia.Reactors;

// SID: Sia Identifier
public partial struct Sid<TId>(TId value)
    where TId : notnull
{
    [Sia]
    public TId Value {
        readonly get => _value;
        set {
            Previous = _value;
            _value = value;
        }
    }

    public TId Previous { get; private set; }

    private TId _value = value;
}

public static class Sid
{
    public static Sid<TId> From<TId>(in TId id)
        where TId : notnull
        => new(id);
    
    public static void SetSid<TId>(this Entity entity, in TId id)
        where TId : notnull
        => entity.Modify(new Sid<TId>.SetValue(id));
}