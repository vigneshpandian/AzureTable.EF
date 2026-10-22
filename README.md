# AzureTableContext

`AzureTableContext` is a small EF-style library for Azure Table Storage. It provides typed `AzureDbSet<T>` properties, fluent entity mappings, computed partition and row keys, lazy table creation, and dependency-injection registration.

## Requirements

- .NET 10
- An Azure Storage account or [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)

## Installation

Add the NuGet package to your application:

```bash
dotnet add package AzureTableContext
```

## Define an entity and context

Entities must have a public parameterless constructor. Public read/write properties are stored as table columns. Supported property types are `string`, `bool`, `int`, `long`, `double`, `DateTime`, `DateTimeOffset`, `Guid`, `byte[]`, nullable versions of those types, and enums.

```csharp
using Azure.Data.Tables;
using AzureTableContext;

public sealed class Order
{
	public string TenantId { get; set; } = string.Empty;
	public Guid Id { get; set; }
	public string Description { get; set; } = string.Empty;
	public decimal Total { get; set; }
}

public sealed class AppDbContext(TableServiceClient serviceClient)
	: AzureDbContext(serviceClient)
{
	public AzureDbSet<Order> Orders { get; private set; } = null!;

	protected override void OnModelCreating(AzureModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Order>()
			.ToTable("Orders")
			.HasPartitionKey(order => order.TenantId)
			.HasRowKey(order => order.Id.ToString());
	}
}
```

If `ToTable` is omitted, the `AzureDbSet<T>` property name is used as the table name. Every set must configure both a partition key and a row key.

## Register with dependency injection

The context and `TableServiceClient` are registered as singletons. Use a connection string from configuration or a secret store rather than hard-coding production credentials.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAzureTableContext<AppDbContext>(
	builder.Configuration.GetConnectionString("Storage")!);
```

For Azurite, the development connection string is commonly:

```text
UseDevelopmentStorage=true
```

Inject the context into an application service or endpoint:

```csharp
app.MapPost("/orders", async (Order order, AppDbContext db, CancellationToken ct) =>
{
	await db.Orders.AddAsync(order, ct);
	return Results.Created($"/orders/{order.Id}", order);
});
```

## CRUD and queries

All operations are immediate requests to Table Storage; there is no change tracking.

```csharp
await db.Orders.AddAsync(order, ct);       // Fails if the row already exists.
await db.Orders.UpsertAsync(order, ct);    // Inserts or fully replaces the row.

var existing = await db.Orders.FindAsync(
	order.TenantId, order.Id.ToString(), ct);

await db.Orders.DeleteAsync(order, ct);
await db.Orders.DeleteAsync(order.TenantId, order.Id.ToString(), ct);

await foreach (var item in db.Orders.QueryPartitionAsync(order.TenantId, ct))
{
	Console.WriteLine(item.Description);
}
```

For arbitrary OData filters, use `QueryAsync`. Escape user input with the Azure SDK helper:

```csharp
var filter = TableClient.CreateQueryFilter($"PartitionKey eq {tenantId}");
await foreach (var order in db.Orders.QueryAsync(filter, ct))
{
	// Process order.
}
```

Tables are created lazily on the first operation against each set. Invalid keys and unsupported entity property types fail before a request is sent.
