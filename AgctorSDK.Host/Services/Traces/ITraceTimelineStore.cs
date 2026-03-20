using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.Traces
{
    /// <summary>
    /// Durable backend for trace timeline snapshots.
    /// Keeps the dashboard timeline reloadable after the live request has finished.
    /// </summary>
    public interface ITraceTimelineStore
    {
        Task SaveAsync(TraceTimelineResponse timeline, CancellationToken cancellationToken = default);
        Task<TraceTimelineResponse?> GetAsync(string traceId, CancellationToken cancellationToken = default);
    }
}
