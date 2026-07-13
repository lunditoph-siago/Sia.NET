namespace Sia;

public class EntityMap<TValue> : Dictionary<EntityId, TValue>;
public class EntityMap : EntityMap<Entity>;
