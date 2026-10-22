using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzureTableContext.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzureTableContext_RegistersClientAndContextAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddAzureTableContext<TestContext>("UseDevelopmentStorage=true");
        using var provider = services.BuildServiceProvider();

        var firstContext = provider.GetRequiredService<TestContext>();
        var secondContext = provider.GetRequiredService<TestContext>();

        Assert.Same(firstContext, secondContext);
        Assert.NotNull(provider.GetRequiredService<TableServiceClient>());
    }

    [Fact]
    public void AddAzureTableContext_DoesNotReplaceExistingClient()
    {
        var existingClient = new TableServiceClient("UseDevelopmentStorage=true");
        var services = new ServiceCollection().AddSingleton(existingClient);

        services.AddAzureTableContext<TestContext>("UseDevelopmentStorage=true");
        using var provider = services.BuildServiceProvider();

        Assert.Same(existingClient, provider.GetRequiredService<TableServiceClient>());
    }

    [Fact]
    public void AddAzureTableContext_RejectsEmptyConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddAzureTableContext<TestContext>(string.Empty));
    }

    private sealed class TestContext(TableServiceClient serviceClient) : AzureDbContext(serviceClient)
    {
        protected override void OnModelCreating(AzureModelBuilder modelBuilder)
        {
        }
    }
}
