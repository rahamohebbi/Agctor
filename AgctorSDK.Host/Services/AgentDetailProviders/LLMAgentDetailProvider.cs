using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Host.Services.AgentDetailProviders;

/// <summary>
/// Provides LLM (Ollama) configuration detail for LLMAgent (PRD-006).
/// </summary>
public class LLMAgentDetailProvider : IAgentDetailProvider
{
    public string AgentTypeName => "LLMAgent";

    public object? GetDetail(IAgent agent)
    {
        return new
        {
            ollamaApiUrl = LLMAgent.GetConfiguredOllamaApiUrl(),
            defaultModel = LLMAgent.GetConfiguredDefaultModel()
        };
    }
}
