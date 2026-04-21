using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Trace;

/// <summary>
/// Receives one <see cref="ResolveSpanDetail"/> per candidate scored by a resolution actor.
/// Host implementations push the payload onto the existing playground trace timeline as a
/// <c>pm.playground.resolve</c> span (PRD-018 §5.7 U1). Kept transport-free so CLI tests and
/// backend-only deployments can log or ignore spans without pulling in Host types.
/// </summary>
public interface IResolveSpanSink
{
    Task EmitAsync(ResolveSpanDetail detail, CancellationToken cancellationToken = default);
}

/// <summary>Default no-op sink so the subsystem runs without a Host wired in.</summary>
public sealed class NullResolveSpanSink : IResolveSpanSink
{
    public Task EmitAsync(ResolveSpanDetail detail, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
