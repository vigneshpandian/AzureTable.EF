using Azure.Data.Tables;
using Xunit;

namespace AzureTableContext.Tests;

public sealed class AzureDbContextTests
{
    [Fact]
    public void Context_InitializesMappedSetUsingConfiguredTableName()
    {
        var context = new TestContext(new TableServiceClient("UseDevelopmentStorage=true"));

        Assert.NotNull(context.Orders);
        Assert.Equal("Orders", context.Orders.TableName);
    }

    [Fact]
    public void Context_UsesPropertyNameWhenTableNameIsNotConfigured()
    {
        var context = new DefaultTableNameContext(new TableServiceClient("UseDevelopmentStorage=true"));

        Assert.Equal(nameof(DefaultTableNameContext.Orders), context.Orders.TableName);
    }

    [Fact]
    public void Context_ThrowsWhenSetHasNoMapping()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new UnmappedContext(new TableServiceClient("UseDevelopmentStorage=true")));

        Assert.Contains("has no mapping", exception.Message);
    }

    [Fact]
    public void Context_ThrowsWhenMappingHasNoRowKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new IncompleteContext(new TableServiceClient("UseDevelopmentStorage=true")));

        Assert.Contains("No row key configured", exception.Message);
    }

    private sealed class TestContext(TableServiceClient serviceClient) : AzureDbContext(serviceClient)
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

    private sealed class DefaultTableNameContext(TableServiceClient serviceClient) : AzureDbContext(serviceClient)
    {
        public AzureDbSet<Order> Orders { get; private set; } = null!;

        protected override void OnModelCreating(AzureModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasPartitionKey("tenant")
                .HasRowKey(order => order.Id.ToString());
        }
    }

    private sealed class UnmappedContext(TableServiceClient serviceClient) : AzureDbContext(serviceClient)
    {
        public AzureDbSet<Order> Orders { get; private set; } = null!;

        protected override void OnModelCreating(AzureModelBuilder modelBuilder)
        {
        }
    }

    private sealed class IncompleteContext(TableServiceClient serviceClient) : AzureDbContext(serviceClient)
    {
        public AzureDbSet<Order> Orders { get; private set; } = null!;

        protected override void OnModelCreating(AzureModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().HasPartitionKey("tenant");
        }
    }

    private sealed class Order
    {
        public string TenantId { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
