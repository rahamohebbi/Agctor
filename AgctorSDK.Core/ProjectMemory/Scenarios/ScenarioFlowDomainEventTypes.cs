namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>PRD-024 domain events that resume <c>AwaitEvent</c> nodes.</summary>
public static class ScenarioFlowDomainEventTypes
{
    public const string VisualExtractCompleted = "visual.extract.completed";
    public const string VisualExtractFailed = "visual.extract.failed";
    public const string InboxConfirmed = "inbox.confirmed";
}
