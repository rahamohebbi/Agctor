namespace AgctorSDK.Core.Rag;

/// <summary>Health state for dashboard badges and fallback decisions.</summary>
public enum RagHealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unavailable = 2,

    /// <summary>Provider selected but Phase 2+ transport not configured yet.</summary>
    NotConfigured = 3
}
