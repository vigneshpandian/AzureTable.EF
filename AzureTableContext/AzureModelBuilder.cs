namespace AzureTableContext;

/// <summary>
/// Collects entity mappings during <see cref="AzureDbContext"/> construction.
/// Equivalent in spirit to EF Core's ModelBuilder (named differently to avoid clashing with it).
/// </summary>
public sealed class AzureModelBuilder
{
    private readonly Dictionary<Type, IEntityMap> _maps = new();

    /// <summary>
    /// Starts (or continues) configuring the mapping for <typeparamref name="T"/>.
    /// Calling it twice for the same type returns a builder over the same mapping.
    /// </summary>
    /// <typeparam name="T">The entity type to configure.</typeparam>
    /// <returns>A fluent builder for the entity.</returns>
    public EntityBuilder<T> Entity<T>() where T : class
    {
        if (!_maps.TryGetValue(typeof(T), out var map))
        {
            map = new EntityMap<T>();
            _maps[typeof(T)] = map;
        }

        return new EntityBuilder<T>((EntityMap<T>)map);
    }

    /// <summary>Returns the configured map for an entity type, or null if none was configured.</summary>
    internal IEntityMap? GetMap(Type entityType) => _maps.GetValueOrDefault(entityType);
}

/// <summary>Fluent API for configuring a single entity's table name and keys.</summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class EntityBuilder<T> where T : class
{
    private readonly EntityMap<T> _map;

    internal EntityBuilder(EntityMap<T> map) => _map = map;

    /// <summary>Sets the Azure table name. If omitted, the DbSet property name is used.</summary>
    /// <param name="tableName">3-63 alphanumeric characters, starting with a letter.</param>
    public EntityBuilder<T> ToTable(string tableName)
    {
        _map.TableName = tableName;
        return this;
    }

    /// <summary>Computes the PartitionKey from each entity, e.g. <c>x =&gt; x.CreatedDate.ToString("yyyyMM")</c>.</summary>
    /// <param name="selector">Pure function returning a valid, non-empty key.</param>
    public EntityBuilder<T> HasPartitionKey(Func<T, string> selector)
    {
        _map.PartitionKey = selector ?? throw new ArgumentNullException(nameof(selector));
        return this;
    }

    /// <summary>Uses one fixed PartitionKey for every row. Fine for small, config-like tables.</summary>
    /// <param name="constant">The partition key shared by all rows.</param>
    public EntityBuilder<T> HasPartitionKey(string constant)
    {
        ArgumentException.ThrowIfNullOrEmpty(constant);
        _map.PartitionKey = _ => constant;
        return this;
    }

    /// <summary>Computes the RowKey from each entity, e.g. <c>x =&gt; x.Id.ToString()</c>.</summary>
    /// <param name="selector">Pure function returning a key unique within its partition.</param>
    public EntityBuilder<T> HasRowKey(Func<T, string> selector)
    {
        _map.RowKey = selector ?? throw new ArgumentNullException(nameof(selector));
        return this;
    }

    /// <summary>Uses one fixed RowKey. Only valid when each partition holds a single row.</summary>
    /// <param name="constant">The row key shared by all rows.</param>
    public EntityBuilder<T> HasRowKey(string constant)
    {
        ArgumentException.ThrowIfNullOrEmpty(constant);
        _map.RowKey = _ => constant;
        return this;
    }
}