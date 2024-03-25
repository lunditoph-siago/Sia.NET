namespace Sia.Tests.Components;

public readonly record struct ObjectId(int Value)
{
    public static implicit operator ObjectId(int id)
        => new(id);
}

public record struct Name([Sia] string Value)
{
    public static implicit operator Name(string name)
        => new(name);
}

[SiaBundle]
public partial record struct GameObject(Sid<ObjectId> Id, Name Name);