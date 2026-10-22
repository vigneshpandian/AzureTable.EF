using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzureTableContext;

/// <summary>Dependency injection helpers for registering an <see cref="AzureDbContext"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared <see cref="TableServiceClient"/> and the context as singletons.
    /// Both are thread-safe and designed to be reused for the life of the app.
    /// </summary>
    /// <typeparam name="TContext">Your context type; its constructor must accept a <see cref="TableServiceClient"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The storage account (or Azurite) connection string.</param>
    public static IServiceCollection AddAzureTableContext<TContext>(
        this IServiceCollection services, string connectionString)
        where TContext : AzureDbContext
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        services.TryAddSingleton(new TableServiceClient(connectionString));
        services.AddSingleton<TContext>();
        return services;
    }
}