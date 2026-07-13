using AgctorSDK.Core.Rag;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

public class RagProviderConfigSchemaTests
{
    [Theory]
    [InlineData(RagProviderIds.None, false)]
    [InlineData(RagProviderIds.LightRag, true)]
    [InlineData(RagProviderIds.Graphiti, true)]
    [InlineData(RagProviderIds.Cognee, true)]
    public void DockerBacked_matches_provider(string id, bool expected)
        => RagProviderConfigSchema.DockerBackedProviders.Contains(id).Should().Be(expected);

    [Fact]
    public void LightRag_fields_include_base_url_and_mode()
    {
        var fields = RagProviderConfigSchema.GetFields(RagProviderIds.LightRag);
        fields.Select(f => f.Key).Should().Contain(new[] { "BaseUrl", "DefaultMode", "Transport" });
    }

    [Fact]
    public void Graphiti_fields_include_base_url_and_group()
    {
        var fields = RagProviderConfigSchema.GetFields(RagProviderIds.Graphiti);
        fields.Select(f => f.Key).Should().Contain(new[] { "BaseUrl", "DefaultGroupId", "Transport" });
    }

    [Fact]
    public void Cognee_fields_include_mcp_path_and_search_type()
    {
        var fields = RagProviderConfigSchema.GetFields(RagProviderIds.Cognee);
        fields.Select(f => f.Key).Should().Contain(new[] { "McpPath", "SearchType", "BaseUrl" });
    }

    [Theory]
    [InlineData(RagProviderIds.LightRag, "lightrag")]
    [InlineData(RagProviderIds.Graphiti, "graphiti")]
    [InlineData(RagProviderIds.Cognee, "cognee-mcp")]
    public void Docker_service_names_match_compose_plan(string providerId, string serviceName)
    {
        RagProviderConfigSchema.GetDockerServiceName(providerId).Should().Be(serviceName);
    }
}
