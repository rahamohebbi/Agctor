using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Resolution;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Observability;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgctorSDK.Core.DependencyInjection;

/// <summary>
/// DI registrations for the PRD-018 entity-resolution subsystem. Kept minimal and side-effect free
/// — spawning the per-project supervisor is a caller concern (see <see cref="ResolutionBootstrapper"/>
/// and, in the Host, <c>ResolutionHostedService</c>).
/// </summary>
public static class ResolutionServiceExtensions
{
    /// <summary>
    /// Registers signal producers, the default actor addressing, metrics, and the bootstrapper.
    /// The <see cref="IResolutionIntentSink"/> registration is composed of a
    /// <see cref="SidecarIntentSink"/> + <see cref="MemoryIntentBridgeSink"/> so operators get both
    /// a git-diffable <c>outgoing.yaml</c> and a machine-readable proposal for the ingest bridge.
    /// Callers can override any registration by adding a different implementation before or after
    /// calling this method.
    /// </summary>
    public static IServiceCollection AddAgctorResolution(this IServiceCollection services)
    {
        services.TryAddSingleton<IResolutionActorAddressing, DefaultResolutionAddressing>();
        services.TryAddSingleton<ResolutionMetrics>();
        services.TryAddSingleton<IResolveSpanSink, NullResolveSpanSink>();
        // Default to a no-op embedding provider so the EmbeddingSimilarity producer can resolve
        // via DI even when the host has not wired a real provider. Real providers (Ollama, OpenAI)
        // replace this registration with `services.AddSingleton<IEmbeddingProvider, …>()` before
        // calling AddAgctorResolution, or swap it afterwards since TryAdd only adds when missing.
        services.TryAddSingleton<IEmbeddingProvider, NullEmbeddingProvider>();
        services.TryAddSingleton<SessionMentionAccumulator>();
        services.TryAddSingleton<MentionObservationPublisher>(sp => new MentionObservationPublisher(
            sp.GetRequiredService<IActorRuntimeAdapter>(),
            sp.GetService<IResolutionActorAddressing>(),
            sp.GetService<SessionMentionAccumulator>()));
        services.TryAddSingleton<SessionSummaryEmitter>(sp => new SessionSummaryEmitter(
            sp.GetRequiredService<IActorRuntimeAdapter>(),
            sp.GetService<IResolutionActorAddressing>()));

        // Default producers (per PRD §5.3). Order is cosmetic — the confidence calculator does the math.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalProducer, AliasMatcher>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalProducer, SurfaceUniqueness>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalProducer, AttributeOverlap>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalProducer, GraphConsistency>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalProducer, EmbeddingSimilarity>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalProducer, NegativeAssertions>());

        services.TryAddSingleton<ResolutionBootstrapper>(sp =>
        {
            var runtime = sp.GetRequiredService<IActorRuntimeAdapter>();
            var loader = sp.GetRequiredService<IProjectLoader>();
            var entities = sp.GetRequiredService<IEntityRegistry>();
            var producers = sp.GetServices<ISignalProducer>().ToList();
            var addressing = sp.GetRequiredService<IResolutionActorAddressing>();
            var metrics = sp.GetService<ResolutionMetrics>();

            return new ResolutionBootstrapper(
                runtime, loader, entities, producers, addressing,
                sinkFactory: projectRoot => BuildSink(projectRoot),
                metrics: metrics,
                spanSink: sp.GetService<IResolveSpanSink>());
        });

        return services;
    }

    /// <summary>
    /// Default composite sink: a sidecar <c>outgoing.yaml</c> + machine-readable intent files.
    /// Stays deterministic so tests and dogfooding produce the same artifacts.
    /// </summary>
    public static IResolutionIntentSink BuildSink(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return new NullResolutionIntentSink();

        string? HostResolver(string withinEntityKey) =>
            ResolveHostRoot(projectRoot, withinEntityKey);

        var sinks = new List<IResolutionIntentSink>
        {
            new SidecarIntentSink(HostResolver),
            new MemoryIntentBridgeSink(projectRoot)
        };
        return new CompositeResolutionIntentSink(sinks);
    }

    private static string? ResolveHostRoot(string projectRoot, string? withinEntityKey)
    {
        if (string.IsNullOrWhiteSpace(withinEntityKey)) return null;
        // Prefer a canonical <projectRoot>/people/<key> folder, then any scenarios/.../people/<key>.
        var canonical = Path.Combine(projectRoot, "people", withinEntityKey);
        if (Directory.Exists(canonical)) return canonical;
        var scenarios = Path.Combine(projectRoot, "scenarios");
        if (Directory.Exists(scenarios))
        {
            foreach (var scen in Directory.EnumerateDirectories(scenarios))
            {
                var candidate = Path.Combine(scen, "people", withinEntityKey);
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
