using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// An agent that interacts with a human user via the CLI to get input.
    /// Implements the Human Agent Fallback feature (prd-cli-001.md).
    /// This agent, when processed, will ask for human input via the IActorRuntimeAdapter.
    /// The human's textual response is then considered the result of this agent's task.
    /// </summary>
    public class HumanAgentAdapter : Agent 
    {
        /// <summary>
        /// Gets the text response provided by the human.
        /// This is populated after the agent successfully processes its prompt and receives input.
        /// This property can be accessed by the parent agent after this agent completes its task.
        /// </summary>
        public string? HumanResponse { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HumanAgentAdapter"/> class.
        /// The ID is typically set by the <see cref="IAgentFactory"/> upon creation.
        /// </summary>
        public HumanAgentAdapter() : base() 
        {
            // Base constructor handles basic setup.
            // ActorType will be "HumanAgentAdapter".
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HumanAgentAdapter"/> class with a specific ID.
        /// </summary>
        /// <param name="id">The unique identifier for this agent instance.</param>
        public HumanAgentAdapter(string id) : base(id)
        {
            // Base constructor handles ID assignment.
        }

        /// <summary>
        /// Core processing logic for the HumanAgentAdapter.
        /// This method is called by the base class's ProcessPromptAsync.
        /// It changes the agent status to WaitingForHumanInput, requests input via the runtime,
        /// stores the response, and then changes status to Completed or Failed.
        /// </summary>
        /// <param name="prompt">The prompt to display to the human user (passed from CurrentPrompt).</param>
        /// <param name="cancellationToken">Token for cancelling the operation.</param>
        protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            // The base Agent.ProcessPromptAsync sets CurrentPrompt and initial AgentStatus to Idle.
            // We need to set the status to Processing here before continuing
            ChangeAgentStatus(AgentStatus.Processing, "Processing prompt");
            LogInfo($"'{Id}' starting to request human input for: \"{prompt}\"");
            
            try
            {
                // The AgentFactory is injected into the base Agent class instance by the factory itself.
                // We need the RuntimeAdapter from the AgentFactory.
                if (AgentFactory == null)
                {
                    LogError("AgentFactory is null. Cannot access IActorRuntimeAdapter.");
                    // Do not change status to WaitingForHumanInput if the factory isn't even set.
                    throw new InvalidOperationException("AgentFactory not initialized in HumanAgentAdapter. This should be set by the spawning mechanism.");
                }

                // AgentFactory is available, now change status and proceed.
                ChangeAgentStatus(AgentStatus.WaitingForHumanInput, "Awaiting human response for the prompt.");

                var runtimeAdapter = AgentFactory.RuntimeAdapter;
                
                // Define standard instructions for the user, as per prd-cli-001.md
                string instructions = "Please enter your response below. Type '::done' on a new line to finish.";
                
                // Request human input via the runtime adapter.
                // The runtime adapter (e.g., InMemoryActorRuntime) will handle the actual CLI interaction.
                string humanInputText = await runtimeAdapter.RequestHumanInputAsync(Id, prompt, instructions, cancellationToken);
                
                // Store the received input as the agent's result.
                HumanResponse = humanInputText; 

                LogInfo($"'{Id}' received human input: \"{HumanResponse.Substring(0, Math.Min(50, HumanResponse.Length))}{(HumanResponse.Length > 50 ? "..." : "")}\"");
                // Change status to completed, indicating the agent has finished its task.
                ChangeAgentStatus(AgentStatus.Completed, "Human response received successfully.");
            }
            catch (OperationCanceledException)
            {
                LogError($"'{Id}' human input request was cancelled for prompt: \"{prompt}\"");
                // Change status to Failed due to cancellation.
                ChangeAgentStatus(AgentStatus.Failed, "Human input request was cancelled by the user or system.");
                throw; // Re-throw so the cancellation is propagated.
            }
            catch (Exception ex)
            {
                LogError($"'{Id}' failed to get human input for prompt \"{prompt}\": {ex.Message}");
                HumanResponse = null; // Ensure response is null on failure.
                // Change status to Failed due to an unexpected error.
                ChangeAgentStatus(AgentStatus.Failed, $"Error during human input acquisition: {ex.Message}");
                throw; // Re-throw the exception to allow higher-level error handling.
            }
        }
        
        /// <summary>
        /// HumanAgentAdapter is a leaf agent and does not assign subtasks to other agents.
        /// Overriding to prevent accidental use and clarify its role.
        /// </summary>
        /// <param name="subtaskPrompt">The prompt for the subtask.</param>
        /// <param name="agentType">The type of agent for the subtask.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Throws NotSupportedException.</returns>
        public override Task<string> AssignSubtaskAsync(string subtaskPrompt, string? agentType = null, CancellationToken cancellationToken = default)
        {
            LogWarning($"'{Id}' (HumanAgentAdapter) received AssignSubtaskAsync call, which is not supported. Prompt: \"{subtaskPrompt}\"");
            throw new NotSupportedException("HumanAgentAdapter is designed to interact with a human user and does not support assigning subtasks to other agents.");
        }

        // No need to override HandleSubtaskCompletionAsync or HandleSubtaskFailureAsync
        // as this agent does not spawn children.

        // The HumanResponse property will serve as the "result" of this agent.
        // The Agctor framework should make this result available to the parent agent 
        // when HandleSubtaskCompletionAsync is called on the parent.
    }
} 