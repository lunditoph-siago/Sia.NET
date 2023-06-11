namespace Sia;

public interface ISystem
{
    static virtual ISystemUnion? Children { get; }
    static virtual ISystemUnion? Dependencies { get; }
    static virtual ITypeUnion? Components { get; }
    static virtual IEnumerable<ICommand>? Triggers { get; }

    void BeforeExecute(World world, Scheduler scheduler) {}
    void Execute(World world, Scheduler scheduler, EntityRef entity) {}
}