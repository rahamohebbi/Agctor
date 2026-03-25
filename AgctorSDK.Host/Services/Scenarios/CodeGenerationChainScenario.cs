using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Scenario that creates a code generation chain:
/// Root Agent -> LLM Agent (with CodeExecutor tool access) -> Return code to user
/// </summary>
public class CodeGenerationChainScenario : IScenario
{
    private readonly IActorRuntimeAdapter _runtimeAdapter;
    private readonly IAgentFactory _agentFactory;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IAgentTypeEnablementService _enablement;
    private readonly ILogger<CodeGenerationChainScenario> _logger;

    public string Name => "code-generation-chain";
    
    public string Description => "Creates Root Agent -> LLM Agent with CodeExecutor tool for code generation and validation";

    public CodeGenerationChainScenario(
        IActorRuntimeAdapter runtimeAdapter,
        IAgentFactory agentFactory,
        IAgentRegistry agentRegistry,
        IAgentTypeEnablementService enablement,
        ILogger<CodeGenerationChainScenario> logger)
    {
        _runtimeAdapter = runtimeAdapter;
        _agentFactory = agentFactory;
        _agentRegistry = agentRegistry;
        _enablement = enablement;
        _logger = logger;
    }

    public async Task<ScenarioSetupResponse> SetupAsync(Dictionary<string, object>? parameters = null)
    {
        try
        {
            _logger.LogInformation("Setting up code generation chain scenario");

            var createdAgentIds = new List<string>();
            var agentRoles = new Dictionary<string, string>();

            // 1. Create Root Agent
            var rootAgentId = "root-agent";
            var rootAgent = await CreateAgentAsync(rootAgentId, "RootAgent");
            if (rootAgent != null)
            {
                createdAgentIds.Add(rootAgentId);
                agentRoles[rootAgentId] = "Root coordinator for the code generation chain";
                _logger.LogInformation("Created Root Agent: {AgentId}", rootAgentId);
            }

            // 2. Create LLM Agent
            var llmAgentId = "llm-agent";
            var llmAgent = await CreateAgentAsync(llmAgentId, "LLMAgent");
            if (llmAgent != null)
            {
                createdAgentIds.Add(llmAgentId);
                agentRoles[llmAgentId] = "LLM Agent for code generation with CodeExecutor tool access";
                _logger.LogInformation("Created LLM Agent: {AgentId}", llmAgentId);
            }

            // 3. Create CodeExecutor Tool
            var codeExecutorId = "code-executor-tool";
            var codeExecutorTool = await CreateAgentAsync(codeExecutorId, "CodeExecutorTool");
            if (codeExecutorTool != null)
            {
                createdAgentIds.Add(codeExecutorId);
                agentRoles[codeExecutorId] = "Code execution tool for validating generated code";
                _logger.LogInformation("Created CodeExecutor Tool: {AgentId}", codeExecutorId);
            }

            // Set up agent relationships (conceptually - this would depend on your agent implementation)
            // In a full implementation, you might configure the LLM agent to know about and use the CodeExecutor tool

            _logger.LogInformation("Code generation chain scenario setup completed. Created {Count} agents", createdAgentIds.Count);

            return new ScenarioSetupResponse(
                Success: true,
                ScenarioName: Name,
                CreatedAgentIds: createdAgentIds,
                AgentRoles: agentRoles,
                ErrorMessage: null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set up code generation chain scenario");
            return new ScenarioSetupResponse(
                Success: false,
                ScenarioName: Name,
                CreatedAgentIds: new List<string>(),
                AgentRoles: new Dictionary<string, string>(),
                ErrorMessage: ex.Message
            );
        }
    }

    private async Task<IAgent?> CreateAgentAsync(string agentId, string agentType)
    {
        try
        {
            // Scenario uses "RootAgent" label; the registered type key is "Agent" (PRD-010).
            var logicalKey = agentType == "RootAgent" ? "Agent" : agentType;
            if (!_enablement.IsTypeEnabled(logicalKey))
            {
                _logger.LogInformation("Skipping {AgentType} (disabled in dashboard settings)", agentType);
                return null;
            }

            // Default prompt for each agent type
            var prompt = agentType switch
            {
                "RootAgent" => "You are a root coordinator agent responsible for managing the code generation workflow.",
                "LLMAgent" => "You are an LLM agent responsible for generating code based on user requests. You can use tools to validate your code.",
                "CodeExecutorTool" => "You are a code execution tool that can run and validate code snippets.",
                _ => "You are an agent in the AGCTOR system."
            };

            // Create the appropriate agent based on type using SpawnAgentAsync
            IAgent agent = agentType switch
            {
                "RootAgent" => await _agentFactory.SpawnAgentAsync<Agent>(prompt, agentId: agentId),
                "LLMAgent" => await _agentFactory.SpawnAgentAsync<LLMAgent>(prompt, agentId: agentId),
                "CodeExecutorTool" => await _agentFactory.SpawnAgentAsync<CodeExecutorTool>(prompt, agentId: agentId),
                _ => throw new ArgumentException($"Unknown agent type: {agentType}")
            };

            // Register the agent
            await _agentRegistry.RegisterAgentAsync(agent);
            
            return agent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create agent {AgentId} of type {AgentType}", agentId, agentType);
            return null;
        }
    }
} 