using AgctorSDK.Core.ProjectMemory.Rag;
using AgctorSDK.Core.Rag;
using AgctorSDK.Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

public class RagContextServiceTests
{
    [Fact]
    public async Task BuildAppendixAsync_none_provider_returns_empty()
    {
        await using var sp = BuildServices(RagProviderIds.None);
        var svc = sp.GetRequiredService<RagContextService>();
        var result = await svc.BuildAppendixAsync(new RagContextRequest("who is ryan?"));
        result.UsedExternalRag.Should().BeFalse();
        result.Appendix.Should().BeEmpty();
    }

    [Fact]
    public async Task FormatChunks_truncates_at_max_chars()
    {
        var chunks = Enumerable.Range(0, 50)
            .Select(i => new RagContextChunk(new string('x', 500)))
            .ToList();

        var text = RagContextService.FormatChunks(chunks, maxChars: 2000);
        text.Length.Should().BeLessOrEqualTo(2100);
        text.Should().Contain("truncated");
    }

    private static ServiceProvider BuildServices(string defaultProvider)
    {
        var services = new ServiceCollection();
        services.AddAgctorRagProviders(configureOptions: o => o.DefaultProvider = defaultProvider);
        return services.BuildServiceProvider();
    }
}
