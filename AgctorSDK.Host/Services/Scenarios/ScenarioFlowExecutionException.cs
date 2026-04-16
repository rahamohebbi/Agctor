namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>Thrown when the PRD-014 flow runner cannot complete (graph shape, ambiguous router, LLM failure bubbled as message).</summary>
public sealed class ScenarioFlowExecutionException : Exception
{
    public ScenarioFlowExecutionException(string message) : base(message)
    {
    }

    public ScenarioFlowExecutionException(string message, Exception inner) : base(message, inner)
    {
    }
}
