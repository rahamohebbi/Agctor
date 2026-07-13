using System;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.Rag;
using AgctorSDK.Extensions.Rag.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Extensions.Rag;

/// <summary>
/// DI-backed factory for <see cref="IRagProviderAdapter"/> (PRD-025 Phase 1).
/// </summary>
public sealed class RagProviderAdapterFactory : IRagProviderAdapterFactory
{
    private static readonly Dictionary<string, Type> ProviderTypeMap = new(StringComparer.Ordinal)
    {
        [RagProviderIds.None] = typeof(NullRagProviderAdapter),
        [RagProviderIds.LightRag] = typeof(LightRagProviderAdapter),
        [RagProviderIds.Graphiti] = typeof(GraphitiProviderAdapter),
        [RagProviderIds.Cognee] = typeof(CogneeProviderAdapter)
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<RagOptions> _options;

    public RagProviderAdapterFactory(IServiceProvider serviceProvider, IOptionsMonitor<RagOptions> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAvailableProviders() => ProviderTypeMap.Keys.ToList();

    /// <inheritdoc />
    public IRagProviderAdapter CreateProvider(string providerId)
    {
        var canonical = RagProviderIds.Normalize(providerId);
        if (!ProviderTypeMap.TryGetValue(canonical, out var adapterType))
        {
            var available = string.Join(", ", GetAvailableProviders());
            throw new ArgumentException(
                $"Unknown RAG provider '{providerId}'. Available providers: {available}",
                nameof(providerId));
        }

        var adapter = _serviceProvider.GetRequiredService(adapterType) as IRagProviderAdapter;
        if (adapter == null)
        {
            throw new InvalidOperationException(
                $"Failed to create RAG provider '{canonical}'. DI returned null or incompatible type.");
        }

        return adapter;
    }

    /// <inheritdoc />
    public string GetDefaultProviderId() =>
        RagProviderIds.IsKnown(_options.CurrentValue.DefaultProvider)
            ? RagProviderIds.Normalize(_options.CurrentValue.DefaultProvider)
            : RagProviderIds.None;

    /// <inheritdoc />
    public IRagProviderAdapter CreateDefaultProvider() => CreateProvider(GetDefaultProviderId());

    /// <inheritdoc />
    public bool IsProviderAvailable(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return false;

        var canonical = RagProviderIds.Normalize(providerId);
        if (!ProviderTypeMap.TryGetValue(canonical, out var adapterType))
            return false;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            return scope.ServiceProvider.GetService(adapterType) != null;
        }
        catch
        {
            return false;
        }
    }
}
