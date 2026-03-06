namespace AgctorSDK.Host.Models;

/// <summary>
/// Configures a reusable actor tree widget for dashboard pages.
/// </summary>
public class ActorTreeComponentModel
{
    public string ComponentId { get; set; } = "actor-tree";
    public string Title { get; set; } = "Actor tree";
    public string Description { get; set; } = "Solution → Project → File → Class → Method actor hierarchy.";
    public string EmptyMessage { get; set; } = "No actor tree available.";
}
