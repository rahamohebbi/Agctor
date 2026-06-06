namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>PRD-024: persisted runtime actor state for multi-turn scenario flows.</summary>
public enum ScenarioFlowRuntimeStatus
{
    Idle = 0,
    Running,
    WaitingForUserInput,
    WaitingForDomainEvent,
    Completed,
    Failed
}
