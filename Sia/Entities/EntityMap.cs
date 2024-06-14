namespace Sia;

public class EntityMap<TValue> : Dictionary<Entity, TValue>;
public class EntityMap : EntityMap<Entity>;