using System.Reflection;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Data.Tables;

namespace AzureTableContext;

/// <summary>
/// A typed handle to one Azure Table. Converts entities to and from table rows and
/// computes keys using the <see cref="EntityMap{T}"/> configured in OnModelCreating.
/// There is no change tracking: every call is an immediate request to Table Storage.
/// </summary>
/// <typeparam name="T">The entity type stored in the table.</typeparam>
public sealed class AzureDbSet<T> where T : class, new()
{
    private static readonly HashSet<string> ReservedNames =
        new(StringComparer.Ordinal) { "PartitionKey", "RowKey", "Timestamp", "ETag" };

    private static readonly HashSet<Type> SupportedTypes = new()
    {
        typeof(string), typeof(bool), typeof(int), typeof(long), typeof(double),
        typeof(DateTime), typeof(DateTimeOffset), typeof(Guid), typeof(byte[])
    };

    // Resolved once per entity type, not per call.
    private static readonly PropertyInfo[] Properties = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite
                    && p.GetIndexParameters().Length == 0
                    && !ReservedNames.Contains(p.Name))
        .ToArray();

    private readonly TableClient _table;
    private readonly EntityMap<T> _map;
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private volatile bool _tableReady;

    /// <summary>Created by <see cref="AzureDbContext"/> via reflection; not meant to be called directly.</summary>
    /// <param name="table">The client for this set's table.</param>
    /// <param name="map">The key and table mapping for <typeparamref name="T"/>.</param>
    internal AzureDbSet(TableClient table, EntityMap<T> map)
    {
        _table = table;
        _map = map;
        EnsureSupportedProperties();
    }

    /// <summary>The Azure table name this set reads from and writes to.</summary>
    public string TableName => _table.Name;

    /// <summary>
    /// Inserts a new row. Fails if a row with the same PartitionKey and RowKey already exists.
    /// </summary>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="RequestFailedException">Status 409 when the key already exists.</exception>
    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureTableAsync(ct);
        await _table.AddEntityAsync(ToRow(entity), ct);
    }

    /// <summary>
    /// Inserts the row, or fully replaces it if the keys already exist.
    /// Properties that are null on <paramref name="entity"/> are removed from the stored row.
    /// </summary>
    /// <param name="entity">The entity to insert or replace.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpsertAsync(T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureTableAsync(ct);
        await _table.UpsertEntityAsync(ToRow(entity), TableUpdateMode.Replace, ct);
    }

    /// <summary>Point lookup by PartitionKey and RowKey. This is the fastest read Table Storage offers.</summary>
    /// <param name="partitionKey">The partition key of the row.</param>
    /// <param name="rowKey">The row key of the row.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entity, or null if no such row exists.</returns>
    public async Task<T?> FindAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
        return response.HasValue ? FromRow(response.Value!) : null;
    }

    /// <summary>
    /// Streams every entity in one partition. Efficient, since it hits a single partition.
    /// </summary>
    /// <param name="partitionKey">The partition to read.</param>
    /// <param name="ct">Cancellation token.</param>
    public IAsyncEnumerable<T> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        // CreateQueryFilter escapes the value, so keys containing quotes cannot break the filter.
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
        return QueryAsync(filter, ct);
    }

    /// <summary>
    /// Streams entities matching an OData filter. Build the filter with
    /// <see cref="TableClient.CreateQueryFilter(FormattableString)"/> when it includes user input.
    /// Filters that don't include PartitionKey scan the whole table.
    /// </summary>
    /// <param name="odataFilter">e.g. <c>PartitionKey eq '202610' and GoldPrice gt 7000</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    public async IAsyncEnumerable<T> QueryAsync(
        string? odataFilter,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);
        await foreach (var row in _table.QueryAsync<TableEntity>(odataFilter, cancellationToken: ct))
            yield return FromRow(row);
    }

    /// <summary>Deletes the row for this entity's computed keys. Does nothing if it does not exist.</summary>
    /// <param name="entity">The entity whose row should be deleted.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return DeleteAsync(
            ValidateKey(_map.PartitionKey!(entity), "PartitionKey"),
            ValidateKey(_map.RowKey!(entity), "RowKey"),
            ct);
    }

    /// <summary>Deletes a row by its keys, unconditionally. Does nothing if it does not exist.</summary>
    /// <param name="partitionKey">The partition key of the row.</param>
    /// <param name="rowKey">The row key of the row.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);
        await _table.DeleteEntityAsync(partitionKey, rowKey, ETag.All, ct);
    }

    /// <summary>
    /// Creates the table on first use. Safe under concurrent callers; the creation call
    /// is idempotent, and the lock only avoids redundant round trips.
    /// </summary>
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (_tableReady) return;

        await _createLock.WaitAsync(ct);
        try
        {
            if (_tableReady) return;
            await _table.CreateIfNotExistsAsync(ct);
            _tableReady = true;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>Converts an entity to a table row: computes keys, then copies each mapped property.</summary>
    private TableEntity ToRow(T entity)
    {
        var partitionKey = ValidateKey(_map.PartitionKey!(entity), "PartitionKey");
        var rowKey = ValidateKey(_map.RowKey!(entity), "RowKey");

        var row = new TableEntity(partitionKey, rowKey);
        foreach (var property in Properties)
        {
            var value = property.GetValue(entity);
            if (value is null) continue; // null means "no column"
            row[property.Name] = value is Enum ? value.ToString() : value;
        }

        return row;
    }

    /// <summary>Converts a table row back into an entity, matching columns to properties by name.</summary>
    private static T FromRow(TableEntity row)
    {
        var entity = new T();
        foreach (var property in Properties)
        {
            if (!row.TryGetValue(property.Name, out var raw) || raw is null) continue;

            var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object value = raw switch
            {
                // Table Storage returns DateTimeOffset for date columns.
                DateTimeOffset dto when target == typeof(DateTime) => dto.UtcDateTime,
                string s when target.IsEnum => Enum.Parse(target, s),
                _ => raw
            };

            property.SetValue(entity, value);
        }

        return entity;
    }

    /// <summary>
    /// Rejects keys Azure Tables would refuse: empty, too long, or containing
    /// '/', '\', '#', '?' or control characters.
    /// </summary>
    private static string ValidateKey(string? key, string keyName)
    {
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException($"{keyName} for '{typeof(T).Name}' resolved to null or empty.");

        if (key.Length > 512)
            throw new InvalidOperationException($"{keyName} for '{typeof(T).Name}' exceeds 512 characters.");

        foreach (var c in key)
        {
            if (c is '/' or '\\' or '#' or '?' || char.IsControl(c))
                throw new InvalidOperationException(
                    $"{keyName} '{key}' for '{typeof(T).Name}' contains a character Azure Tables does not allow.");
        }

        return key;
    }

    /// <summary>
    /// Fails fast at startup if the entity has a property type Table Storage cannot store
    /// (for example decimal), instead of failing on the first write.
    /// </summary>
    private static void EnsureSupportedProperties()
    {
        foreach (var property in Properties)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type.IsEnum || SupportedTypes.Contains(type)) continue;

            throw new NotSupportedException(
                $"{typeof(T).Name}.{property.Name} has type {property.PropertyType.Name}, which Azure Tables cannot store. " +
                "Supported: string, bool, int, long, double, DateTime, DateTimeOffset, Guid, byte[], enums.");
        }
    }
}