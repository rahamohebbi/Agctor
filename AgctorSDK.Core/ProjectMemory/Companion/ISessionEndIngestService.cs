using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Companion;

/// <summary>
/// Best-effort companion hook: replay new session turns into ProjectMemory on checkpoint/delete.
/// </summary>
public interface ISessionEndIngestService
{
    Task<SessionEndIngestResult> TryIngestOnSessionEndAsync(
        string sessionId,
        string projectRoot,
        SessionEndIngestTrigger trigger,
        CancellationToken cancellationToken = default);
}
