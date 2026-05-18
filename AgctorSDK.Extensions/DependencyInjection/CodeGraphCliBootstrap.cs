using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Snippets;
using AgctorSDK.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>Registers CodeGraph snippet providers the same way the HTTP host does at startup.</summary>
public static class CodeGraphCliBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services, IActorRuntimeAdapter runtime)
    {
        SnippetProviderBootstrapper.RegisterBuiltIn();
        var llmClient = services.GetRequiredService<ILlmClient>();
        var snippetResolver = await runtime.SpawnActorAsync(
            "snippet-resolver-cli",
            id => new SnippetResolverAgent(id, llmClient)).ConfigureAwait(false);
        SnippetProviderRegistry.Register(snippetResolver);
    }
}
