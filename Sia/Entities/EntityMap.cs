namespace Sia;

public class EntityMap<TKey> : Dictionary<TKey, EntityRef>
    where TKey : notnull;

public class EntityMap : EntityMap<Identity>;