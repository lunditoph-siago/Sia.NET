namespace Sia;

public interface IGenericHandler
{
    public void Handle<T>(in T value);
}

public interface IGenericHandler<TBase>
{
    public void Handle<T>(in T value) where T : TBase;
}

public interface IGenericStructHandler<TBase>
{
    public void Handle<T>(in T value) where T : struct, TBase;
}

public interface IGenericTypeHandler
{
    public void Handle<T>();
}

public interface IGenericTypeHandler<TBase>
{
    public void Handle<T>() where T : TBase;
}

public interface IGenericStructTypeHandler<TBase>
{
    public void Handle<T>() where T : struct, TBase;
}

public interface IGenericConcreteTypeHandler<TBase>
{
    public void Handle<T>() where T : TBase, new();
}

public interface IRefGenericHandler
{
    public void Handle<T>(ref T value);
}

public interface IRefGenericHandler<TBase>
{
    public void Handle<T>(ref T value) where T : TBase;
}

public interface IGenericPredicate
{
    public bool Predicate<T>(in T value);
}

public interface IGenericPredicate<TBase>
{
    public bool Predicate<T>(in T value) where T : TBase;
}