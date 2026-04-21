using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Bridge;

/// <summary>
/// Receives <see cref="IngestIntentDraft"/> from the resolution subsystem and either materializes it
/// (real ingest pipeline) or captures it for review. Multiple sinks can be composed so, for example,
/// a sidecar-writer can run alongside a production ingest bridge during rollout.
/// </summary>
public interface IResolutionIntentSink
{
    Task ApplyAsync(IngestIntentDraft draft, CancellationToken cancellationToken = default);
}

/// <summary>Sink that silently swallows drafts. Default when nothing is wired.</summary>
public sealed class NullResolutionIntentSink : IResolutionIntentSink
{
    public Task ApplyAsync(IngestIntentDraft draft, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
