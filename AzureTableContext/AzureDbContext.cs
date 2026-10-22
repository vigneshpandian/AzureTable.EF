using System.Reflection;
using Azure.Data.Tables;

namespace AzureTableContext;

/// <summary>
/// Base class for a table-backed context. Derive from it, declare
/// <see cref="AzureDbSet{T}"/> properties, and configure them in <see cref="OnModelCreating"/>.
/// The sets are created for you; they do not need to be assigned.
/// The context holds no per-request state, so register it as a singleton.
/// </summary>
public abstract class AzureDbContext
{
    /// <summary>
    /// Runs <see cref="OnModelCreating"/>, then creates every <see cref="AzureDbSet{T}"/>
    /// property declared on the derived class.
    /// </summary>
    /// <param name="serviceClient">The shared client for the storage account.</param>
    protected AzureDbContext(TableServiceClient serviceClient)
    {
        ArgumentNullException.ThrowIfNull(serviceClient);

        var modelBuilder = new AzureModelBuilder();
        OnModelCreating(modelBuilder);
        InitializeSets(serviceClient, modelBuilder);
    }

    /// <summary>
    /// Override to configure table names and key selectors for each entity type.
    /// Called once, from the base constructor, so do not rely on derived fields here.
    /// </summary>
    /// <param name="modelBuilder">The builder to configure entities on.</param>
    protected abstract void OnModelCreating(AzureModelBuilder modelBuilder);

    /// <summary>
    /// Finds each AzureDbSet property on the derived type, builds a set with the
    /// matching table client and mapping, and assigns it to the property.
    /// </summary>
    private void InitializeSets(TableServiceClient serviceClient, AzureModelBuilder modelBuilder)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var setProperties = GetType().GetProperties(all).Where(p =>
            p.CanWrite
            && p.PropertyType.IsGenericType
            && p.PropertyType.GetGenericTypeDefinition() == typeof(AzureDbSet<>));

        foreach (var property in setProperties)
        {
            var entityType = property.PropertyType.GetGenericArguments()[0];

            var map = modelBuilder.GetMap(entityType)
                ?? throw new InvalidOperationException(
                    $"'{GetType().Name}.{property.Name}' has no mapping. " +
                    $"Call modelBuilder.Entity<{entityType.Name}>() in OnModelCreating.");

            map.Validate();

            var tableName = map.TableName ?? property.Name;
            var tableClient = serviceClient.GetTableClient(tableName);

            var set = Activator.CreateInstance(
                property.PropertyType, all, binder: null, args: new object[] { tableClient, map }, culture: null);

            property.SetValue(this, set);
        }
    }
}