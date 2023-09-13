namespace Sia;

public class Group : Group<EntityRef> {}

// Events

public interface IEventSender<TEvent> : IEventSender<EntityRef, TEvent>
    where TEvent : IEvent
{
}

public interface IEventSender : IEventSender<IEvent> {}

public class BatchedEvent<TEvent> : BatchedEvent<TEvent, EntityRef>
    where TEvent : IEvent
{
}

public class BatchedEvent : BatchedEvent<IEvent> {}

public class Dispatcher<TEvent> : Dispatcher<EntityRef, TEvent>
    where TEvent : IEvent
{
}

public class Dispatcher : Dispatcher<IEvent> {}

public interface IEventListener : IEventListener<EntityRef> {}

// Commands

public interface ICommand : ICommand<EntityRef> {}
public interface IDeferrableCommand : IDeferrableCommand<EntityRef> {}
public interface ISortableCommand : ISortableCommand<EntityRef> {}

public abstract class Command<TCommand>
    : Command<TCommand, EntityRef>, ICommand
    where TCommand : Command<TCommand>, new()
{
}

public abstract class ImpureCommand<TCommand>
    : ImpureCommand<TCommand, EntityRef>, ICommand
    where TCommand : ImpureCommand<TCommand>, new()
{
}

public abstract class PropertyCommand<TCommand, TValue>
    : PropertyCommand<TCommand, EntityRef, TValue>, ICommand
    where TCommand : PropertyCommand<TCommand, EntityRef, TValue>, new()
{
}

public abstract class ImpurePropertyCommand<TCommand, TValue>
    : ImpurePropertyCommand<TCommand, EntityRef, TValue>, ICommand
    where TCommand : ImpurePropertyCommand<TCommand, TValue>, new()
{
}

public class CommandMailbox<TCommand> : CommandMailbox<EntityRef, TCommand>, IEventSender<TCommand>
    where TCommand : ICommand<EntityRef>
{
}

public class CommandMailbox : CommandMailbox<ICommand<EntityRef>>
{
}

// Worlds

public class World : World<EntityRef>
{
}

public interface IAddonUninitializeListener : IAddonUninitializeListener<EntityRef>
{
}