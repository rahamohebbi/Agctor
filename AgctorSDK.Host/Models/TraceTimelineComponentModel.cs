namespace AgctorSDK.Host.Models;

/// <summary>
/// Configures a reusable trace timeline widget for dashboard pages.
/// </summary>
public class TraceTimelineComponentModel
{
    public string ComponentId { get; set; } = "trace-timeline";
    public string Title { get; set; } = "Trace timeline";
    public string EmptyMessage { get; set; } = "Select a trace to visualize.";
    public int HeightPx { get; set; } = 360;
    public string? InitialTraceId { get; set; }
}
