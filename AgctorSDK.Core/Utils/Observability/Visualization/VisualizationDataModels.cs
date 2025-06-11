using System.Collections.Generic;

namespace AgctorSDK.Core.Utils.Observability.Visualization
{
    /// <summary>
    /// Represents a node in an agent hierarchy visualization.
    /// </summary>
    public class AgentHierarchyNode
    {
        /// <summary>
        /// Gets or sets the ID of the agent.
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the name of the agent.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the type of the agent.
        /// </summary>
        public string Type { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the description of the agent.
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the child agents of this agent.
        /// </summary>
        public List<AgentHierarchyNode> Children { get; set; } = new List<AgentHierarchyNode>();
    }

    /// <summary>
    /// Represents a message flow diagram visualization.
    /// </summary>
    public class MessageFlowDiagram
    {
        /// <summary>
        /// Gets or sets the participants in the message flow.
        /// </summary>
        public List<MessageFlowParticipant> Participants { get; set; } = new List<MessageFlowParticipant>();
        
        /// <summary>
        /// Gets or sets the messages exchanged between participants.
        /// </summary>
        public List<MessageFlowMessage> Messages { get; set; } = new List<MessageFlowMessage>();
    }

    /// <summary>
    /// Represents a participant in a message flow diagram.
    /// </summary>
    public class MessageFlowParticipant
    {
        /// <summary>
        /// Gets or sets the ID of the participant.
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the display name of the participant.
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a message in a message flow diagram.
    /// </summary>
    public class MessageFlowMessage
    {
        /// <summary>
        /// Gets or sets the ID of the source participant.
        /// </summary>
        public string SourceId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the ID of the target participant.
        /// </summary>
        public string TargetId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the message content.
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the duration of the message in milliseconds.
        /// </summary>
        public double DurationMs { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether the message is asynchronous.
        /// </summary>
        public bool IsAsync { get; set; }
    }
} 