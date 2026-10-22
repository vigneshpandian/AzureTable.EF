namespace AzureTableContext;

/// <summary>
/// Non-generic view of an <see cref="EntityMap{T}"/>. Lets <see cref="AzureDbContext"/>
/// read the mapping for any entity type without reflecting over the generic class.
/// </summary>
public interface IEntityMap
{
    /// <summary>The CLR type this map describes.</summary>
    Type EntityType { get; }

    /// <summary>The configured table name, or null to fall back to the DbSet property name.</summary>
    string? TableName { get; }

    /// <summary>Throws if the map is missing anything required to save an entity.</summary>
    void Validate();
}

/// <summary>
/// Describes how an entity type maps to an Azure Table: which table it lives in and
/// how its PartitionKey and RowKey are computed from an instance.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class EntityMap<T> : IEntityMap where T : class
{
    /// <inheritdoc />
    public Type EntityType => typeof(T);

    /// <inheritdoc />
    public string? TableName { get; set; }

    /// <summary>
    /// Computes the PartitionKey for an entity. Runs on every write, so keep it cheap and deterministic.
    /// </summary>
    public Func<T, string>? PartitionKey { get; set; }

    /// <summary>
    /// Computes the RowKey for an entity. PartitionKey + RowKey must be unique per row.
    /// </summary>
    public Func<T, string>? RowKey { get; set; }

    /// <inheritdoc />
    public void Validate()
    {
        if (PartitionKey is null)
            throw new InvalidOperationException(
                $"No partition key configured for '{typeof(T).Name}'. Call HasPartitionKey(...) in OnModelCreating.");

        if (RowKey is null)
            throw new InvalidOperationException(
                $"No row key configured for '{typeof(T).Name}'. Call HasRowKey(...) in OnModelCreating.");
    }
}