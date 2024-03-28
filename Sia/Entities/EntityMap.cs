namespace Sia;

public class EntityMap<TValue> : Dictionary<Identity, TValue>;
public class EntityMap : EntityMap<EntityRef>;