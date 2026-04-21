using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Observability;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Policy;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;

namespace AgctorSDK.Core.ProjectMemory.Resolution;

/// <summary>
/// One-stop helper that loads the policy, discovers entities, spawns the
/// <see cref="ResolutionSupervisorActor"/> for a project, and wires a file watcher so
/// <c>.agctor/resolution.yaml</c> edits hot-reload without a process restart.
/// </summary>
/// <remarks>
/// Kept in Core (not Host) so CLI, tests, and the dashboard can all bootstrap the same way.
/// The bootstrapper is idempotent: calling <see cref="StartAsync"/> twice for the same project
/// shuts the existing supervisor down and re-spawns it (useful after project-root changes).
/// </remarks>
public sealed class ResolutionBootstrapper : IAsyncDisposable
{
    private readonly IActorRuntimeAdapter _runtime;
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IReadOnlyList<ISignalProducer> _producers;
    private readonly IResolutionActorAddressing _addressing;
    private readonly Func<string, IResolutionIntentSink> _sinkFactory;
    private readonly IResolveSpanSink _spanSink;
    private readonly ResolutionMetrics? _metrics;

    private ResolutionSupervisorActor? _supervisor;
    private FileSystemWatcher? _policyWatcher;
    private string? _projectRoot;
    private string? _projectId;
    private readonly object _reloadLock = new();
    private DateTime _lastPolicyReload;

    public ResolutionBootstrapper(
        IActorRuntimeAdapter runtime,
        IProjectLoader loader,
        IEntityRegistry entities,
        IReadOnlyList<ISignalProducer> producers,
        IResolutionActorAddressing? addressing = null,
        Func<string, IResolutionIntentSink>? sinkFactory = null,
        ResolutionMetrics? metrics = null,
        IResolveSpanSink? spanSink = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _producers = producers ?? throw new ArgumentNullException(nameof(producers));
        _addressing = addressing ?? new DefaultResolutionAddressing();
        _sinkFactory = sinkFactory ?? (root => new MemoryIntentBridgeSink(root));
        _spanSink = spanSink ?? new NullResolveSpanSink();
        _metrics = metrics;
    }

    public ResolutionSupervisorActor? Supervisor => _supervisor;
    public string? ProjectId => _projectId;
    public string? ProjectRoot => _projectRoot;

    /// <summary>
    /// Boot or re-boot the subsystem for the given project. Reads policy from disk; if
    /// <c>resolution.yaml</c> is missing or <see cref="ResolutionPolicy.Enabled"/> is false the
    /// supervisor is still spawned but idle (no mentions land on the reconciler).
    /// </summary>
    public async Task StartAsync(string projectRoot, string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentNullException(nameof(projectRoot));
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
        _projectRoot = Path.GetFullPath(projectRoot);
        _projectId = projectId;

        await StopAsync(cancellationToken).ConfigureAwait(false);

        var policy = ResolutionPolicyLoader.Load(_projectRoot);
        if (!Directory.Exists(Path.Combine(_projectRoot, ".agctor")))
            return; // nothing to do for non-agctor folders
        var ctx = await _loader.LoadAsync(_projectRoot, cancellationToken).ConfigureAwait(false);
        var entities = await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);

        var sink = _sinkFactory(_projectRoot);
        _supervisor = await _runtime.SpawnActorAsync(
            _addressing.SupervisorIdFor(_projectId),
            id => new ResolutionSupervisorActor(
                id, _projectId, _projectRoot, _runtime, policy, _producers,
                _addressing, sink, _metrics, _spanSink),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await _supervisor.SpawnAllAsync(entities, cancellationToken).ConfigureAwait(false);

        WirePolicyWatcher();
    }

    /// <summary>
    /// Re-discover entities from the registry and rebuild the mention index. Call this after a
    /// new <c>people/&lt;key&gt;/</c> folder is created mid-session.
    /// </summary>
    public async Task RefreshEntitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_supervisor == null || _projectRoot == null) return;
        var ctx = await _loader.LoadAsync(_projectRoot, cancellationToken).ConfigureAwait(false);
        var entities = await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);
        _supervisor.RebuildIndex(entities);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_policyWatcher != null)
        {
            _policyWatcher.EnableRaisingEvents = false;
            _policyWatcher.Dispose();
            _policyWatcher = null;
        }
        if (_supervisor != null)
        {
            await _supervisor.ShutdownAllAsync(cancellationToken).ConfigureAwait(false);
            _supervisor = null;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private void WirePolicyWatcher()
    {
        if (_projectRoot == null || _supervisor == null) return;
        var dir = Path.Combine(_projectRoot, ".agctor");
        if (!Directory.Exists(dir)) return;

        _policyWatcher = new FileSystemWatcher(dir, "resolution.yaml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _policyWatcher.Changed += (_, _) => TryReloadPolicy();
        _policyWatcher.Created += (_, _) => TryReloadPolicy();
        _policyWatcher.Renamed += (_, _) => TryReloadPolicy();
    }

    private void TryReloadPolicy()
    {
        // Debounce: editors tend to fire 2-3 events per save.
        lock (_reloadLock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastPolicyReload).TotalMilliseconds < 1000) return;
            _lastPolicyReload = now;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (_projectRoot == null || _supervisor == null) return;
                // Tiny delay so the writer finishes flushing before we read.
                await Task.Delay(150).ConfigureAwait(false);
                var latest = ResolutionPolicyLoader.Load(_projectRoot);
                await _supervisor.ReloadPolicyAsync(latest, changedBy: "policy-watcher").ConfigureAwait(false);
            }
            catch
            {
                // Policy reloads are best-effort: file corruption leaves the old policy in place.
            }
        });
    }
}
