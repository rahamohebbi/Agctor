using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using Microsoft.Extensions.Hosting;

namespace AgctorSDK.Host.Services;

/// <summary>Hot-swaps the active <see cref="IActorRuntimeAdapter"/> when the dashboard changes actor model.</summary>
public interface IActorRuntimeSwitchService
{
    Task InitializeFromConfigurationAsync(CancellationToken cancellationToken = default);
    Task<ActorRuntimeSwitchResult> SwitchToAsync(
        string canonicalRuntimeId,
        bool? allowExperimentalRuntimes = null,
        CancellationToken cancellationToken = default);
}

public sealed class ActorRuntimeSwitchResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string LiveRuntimeId { get; init; } = "";
}

/// <inheritdoc />
public sealed class ActorRuntimeSwitchService : IActorRuntimeSwitchService
{
    private readonly SwitchableActorRuntimeAdapter _switchable;
    private readonly IActorRuntimeAdapterFactory _factory;
    private readonly IAgentRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ActorRuntimeSwitchService> _logger;

    public ActorRuntimeSwitchService(
        SwitchableActorRuntimeAdapter switchable,
        IActorRuntimeAdapterFactory factory,
        IAgentRegistry registry,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ActorRuntimeSwitchService> logger)
    {
        _switchable = switchable;
        _factory = factory;
        _registry = registry;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InitializeFromConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var configured = AgctorRuntimeCatalog.NormalizeRuntimeName(
            _configuration.GetValue<string>("Agctor:DefaultRuntime")) ?? AgctorRuntimeCatalog.InMemory;
        var allowExperimental = _configuration.GetValue("Agctor:AllowExperimentalRuntimes", false);
        if (AgctorRuntimeCatalog.IsExperimental(configured) && !allowExperimental)
            configured = AgctorRuntimeCatalog.InMemory;

        await SwitchToAsync(configured, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ActorRuntimeSwitchResult> SwitchToAsync(
        string canonicalRuntimeId,
        bool? allowExperimentalRuntimes = null,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeSelectionNormalizer.TryNormalize(canonicalRuntimeId, _factory, out var canonical, out var err))
        {
            return new ActorRuntimeSwitchResult { Success = false, Message = err ?? "Invalid runtime." };
        }

        var allowExperimental = allowExperimentalRuntimes
            ?? _configuration.GetValue("Agctor:AllowExperimentalRuntimes", false);
        if (AgctorRuntimeCatalog.IsExperimental(canonical) && !allowExperimental)
        {
            return new ActorRuntimeSwitchResult
            {
                Success = false,
                Message = "Enable AllowExperimentalRuntimes to use Orleans or Proto.Actor."
            };
        }

        var current = _switchable.Current;
        if (string.Equals(RuntimeCanonicalId.FromAdapter(current), canonical, StringComparison.OrdinalIgnoreCase)
            && current.IsInitialized)
        {
            return new ActorRuntimeSwitchResult
            {
                Success = true,
                LiveRuntimeId = canonical,
                Message = $"{canonical} is already active."
            };
        }

        try
        {
            await StopRegisteredAgentsAsync(current, cancellationToken).ConfigureAwait(false);
            if (current.IsInitialized)
                await current.ShutdownAsync(cancellationToken).ConfigureAwait(false);

            var next = _factory.CreateRuntime(canonical);
            var config = ActorRuntimeConfigBuilder.FromConfiguration(_configuration, canonical, _environment.EnvironmentName);
            await next.InitializeAsync(config, cancellationToken).ConfigureAwait(false);
            _switchable.SetInner(next);

            _logger.LogInformation("Switched actor runtime to {Runtime}", canonical);
            return new ActorRuntimeSwitchResult
            {
                Success = true,
                LiveRuntimeId = canonical,
                Message = $"Now using {canonical}. Existing agents were stopped; re-apply your scenario if needed."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch actor runtime to {Runtime}", canonical);
            return new ActorRuntimeSwitchResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private async Task StopRegisteredAgentsAsync(IActorRuntimeAdapter adapter, CancellationToken cancellationToken)
    {
        var ids = (await _registry.GetAllAgentIdsAsync().ConfigureAwait(false)).ToList();
        foreach (var id in ids)
        {
            try
            {
                if (adapter.IsInitialized)
                    await adapter.StopActorAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "StopActorAsync failed for {AgentId} during runtime switch", id);
            }

            try
            {
                await _registry.UnregisterAgentAsync(id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UnregisterAgentAsync failed for {AgentId} during runtime switch", id);
            }
        }
    }
}
