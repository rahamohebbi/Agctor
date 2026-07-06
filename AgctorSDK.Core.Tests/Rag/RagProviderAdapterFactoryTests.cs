using AgctorSDK.Core.Rag;
using AgctorSDK.Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

public class RagProviderAdapterFactoryTests
{
    private static ServiceProvider BuildServices(Action<RagOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddAgctorRagProviders(configureOptions: o =>
        {
            o.DefaultProvider = RagProviderIds.LightRag;
            configure?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void GetAvailableProviders_lists_v1_ids()
    {
        using var sp = BuildServices();
        var factory = sp.GetRequiredService<IRagProviderAdapterFactory>();
        factory.GetAvailableProviders().Should().Equal(RagProviderIds.All);
    }

    [Fact]
    public void CreateProvider_None_returns_null_adapter()
    {
        using var sp = BuildServices();
        var factory = sp.GetRequiredService<IRagProviderAdapterFactory>();
        var adapter = factory.CreateProvider(RagProviderIds.None);
        adapter.ProviderId.Should().Be(RagProviderIds.None);
    }

    [Fact]
    public void CreateDefaultProvider_uses_configured_default()
    {
        using var sp = BuildServices();
        var factory = sp.GetRequiredService<IRagProviderAdapterFactory>();
        factory.GetDefaultProviderId().Should().Be(RagProviderIds.LightRag);
        factory.CreateDefaultProvider().ProviderId.Should().Be(RagProviderIds.LightRag);
    }

    [Fact]
    public void CreateProvider_unknown_throws()
    {
        using var sp = BuildServices();
        var factory = sp.GetRequiredService<IRagProviderAdapterFactory>();
        var act = () => factory.CreateProvider("RAGFlow");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsProviderAvailable_true_for_registered_adapters()
    {
        using var sp = BuildServices();
        var factory = sp.GetRequiredService<IRagProviderAdapterFactory>();
        factory.IsProviderAvailable("cognee").Should().BeTrue();
    }

    [Fact]
    public async Task Null_adapter_query_returns_empty_chunks()
    {
        await using var sp = BuildServices();
        var adapter = sp.GetRequiredService<IRagProviderAdapterFactory>().CreateProvider(RagProviderIds.None);
        var result = await adapter.QueryAsync(new RagQueryRequest("who is ryan?", null));
        result.Chunks.Should().BeEmpty();
    }
}
