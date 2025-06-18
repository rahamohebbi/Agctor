using System.Collections.Generic;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Options for configuring an agent instance.
    /// </summary>
    public class AgentOptions
    {
        /// <summary>
        /// Gets or sets optional parent agent ID for hierarchical agent relationships.
        /// </summary>
        public string? ParentAgentId { get; set; }

        /// <summary>
        /// Gets or sets the initial prompt for the agent.
        /// </summary>
        public string? InitialPrompt { get; set; }

        /// <summary>
        /// Gets or sets additional configuration parameters for the agent.
        /// </summary>
        public IDictionary<string, object> Parameters { get; }

        /// <summary>
        /// Initializes a new instance of the AgentOptions class.
        /// </summary>
        public AgentOptions()
        {
            Parameters = new Dictionary<string, object>();
        }
    }
} 