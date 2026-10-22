using Azure.Data.Tables;
using Xunit;

namespace AzureTableContext.Tests;

public sealed class AzureModelBuilderTests
{
    [Fact]
    public void Entity_ReturnsBuilderOverTheSameMappingWhenConfiguredMoreThanOnce()
    {
        var context = new ReconfiguredContext(new TableServiceClient("UseDevelopmentStorage=true"));

        Assert.Equal("Orders", context.Orders.TableName);
    }

    [Fact]
    public void EntityBuilder_RejectsNullSelectorsAndEmptyConstants()
    {
        var entityBuilder = new AzureModelBuilder().Entity<TestEntity>();

        Assert.Throws<ArgumentNullException>(() => entityBuilder.HasPartitionKey((Func<TestEntity, string>)null!));
        Assert.Throws<ArgumentNullException>(() => entityBuilder.HasRowKey((Func<TestEntity, string>)null!));
        Assert.Throws<ArgumentException>(() => entityBuilder.HasPartitionKey(string.Empty));
        Assert.Throws<ArgumentException>(() => entityBuilder.HasRowKey(string.Empty));
    }

    [Fact]
    public void EntityMap_ValidateRequiresBothKeys()
    {
        var map = new EntityMap<TestEntity>();

        var partitionError = Assert.Throws<InvalidOperationException>(() => map.Validate());
        Assert.Contains("partition key", partitionError.Message, StringComparison.OrdinalIgnoreCase);

        map.PartitionKey = _ => "tenant";
        var rowError = Assert.Throws<InvalidOperationException>(() => map.Validate());
        Assert.Contains("row key", rowError.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestEntity
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class ReconfiguredContext(TableServiceClient serviceClient) : AzureDbContext(serviceClient)
    {
        public AzureDbSet<TestEntity> Orders { get; private set; } = null!;

        protected override void OnModelCreating(AzureModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>()
                .HasPartitionKey("tenant")
                .HasRowKey(entity => entity.Id);
            modelBuilder.Entity<TestEntity>().ToTable("Orders");
        }
    }
}
