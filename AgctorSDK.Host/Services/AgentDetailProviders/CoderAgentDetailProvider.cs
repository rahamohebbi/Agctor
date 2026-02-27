using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Host.Services.AgentDetailProviders;

/// <summary>
/// Provides tools pipeline detail for CoderAgent (PRD-006). CoderAgent uses Edit → Compile → Test.
/// </summary>
public class CoderAgentDetailProvider : IAgentDetailProvider
{
    public string AgentTypeName => "CoderAgent";

    public object? GetDetail(IAgent agent)
    {
        return new
        {
            tools = new[] { "CodeEditorTool", "CompileTool", "TestRunnerTool" },
            pipeline = "Edit → Compile → Test"
        };
    }
}
