using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.Utils.Observability.Visualization
{
    /// <summary>
    /// Service for visualizing agent hierarchies, message flows, and other aspects of the Agctor system.
    /// </summary>
    public class VisualizationService : IVisualizationService
    {
        private readonly IAgentRegistry? _agentRegistry;
        private readonly IActivityTracker? _activityTracker;
        private readonly IAgctorLogger _logger;
        private readonly VisualizationOptions _options;

        /// <summary>
        /// Initializes a new instance of the VisualizationService class.
        /// </summary>
        /// <param name="agentRegistry">Registry for accessing agent information</param>
        /// <param name="activityTracker">Activity tracker for accessing traces</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="options">Visualization configuration options</param>
        private VisualizationService(
            IAgentRegistry agentRegistry,
            IActivityTracker activityTracker,
            IAgctorLogger logger,
            VisualizationOptions options)
        {
            _agentRegistry = agentRegistry;
            _activityTracker = activityTracker;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new VisualizationOptions();
        }

        /// <summary>
        /// Initializes a new instance of the VisualizationService class with options from DI.
        /// </summary>
        /// <param name="agentRegistry">Registry for accessing agent information</param>
        /// <param name="activityTracker">Activity tracker for accessing traces</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="options">Visualization configuration options</param>
        public VisualizationService(
            IAgentRegistry agentRegistry,
            IActivityTracker activityTracker,
            IAgctorLogger logger,
            IOptions<VisualizationOptions>? options = null) 
            : this(agentRegistry, activityTracker, logger, options?.Value ?? new VisualizationOptions())
        {
        }

        /// <inheritdoc />
        public async Task<string> GenerateAgentHierarchyMermaidDiagramAsync(string rootAgentId)
        {
            _logger.Info($"Generating agent hierarchy diagram for root agent: {rootAgentId}");
            
            if (_agentRegistry == null)
            {
                _logger.Warning("Agent registry is not available, generating placeholder diagram");
                return GeneratePlaceholderAgentHierarchy(rootAgentId);
            }
            
            try
            {
                // Build a hierarchical structure of agents
                var agentHierarchy = await BuildAgentHierarchyAsync(rootAgentId);
                
                // Generate the Mermaid diagram
                return GenerateMermaidDiagramFromHierarchy(agentHierarchy);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to generate agent hierarchy diagram: {ex.Message}");
                return GeneratePlaceholderAgentHierarchy(rootAgentId);
            }
        }

        /// <inheritdoc />
        public async Task<string> GenerateMessageFlowMermaidDiagramAsync(string traceId)
        {
            _logger.Info($"Generating message flow diagram for trace: {traceId}");
            
            if (_activityTracker == null)
            {
                _logger.Warning("Activity tracker is not available, generating placeholder diagram");
                return GeneratePlaceholderMessageFlow(traceId);
            }
            
            try
            {
                // Get the trace activities
                var activities = await _activityTracker.GetTraceActivitiesAsync(traceId);
                
                if (activities == null || !activities.Any())
                {
                    _logger.Warning($"No activities found for trace ID: {traceId}");
                    return GeneratePlaceholderMessageFlow(traceId);
                }
                
                // Build a message flow model
                var messageFlow = BuildMessageFlowFromActivities(activities);
                
                // Generate the Mermaid diagram
                return GenerateMermaidDiagramFromMessageFlow(messageFlow);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to generate message flow diagram: {ex.Message}");
                return GeneratePlaceholderMessageFlow(traceId);
            }
        }

        /// <inheritdoc />
        public async Task<string> GenerateVisualizationHtmlAsync(string? rootAgentId = null, string? traceId = null)
        {
            _logger.Info("Generating visualization HTML");
            
            var html = new StringBuilder();
            html.AppendLine("<div class=\"agctor-visualization\">");
            
            // Add external trace viewer links if available
            if (!string.IsNullOrEmpty(traceId))
            {
                var traceViewerUrl = GetTraceViewerUrl(traceId);
                if (!string.IsNullOrEmpty(traceViewerUrl))
                {
                    html.AppendLine("<div class=\"viz-links\">");
                    html.AppendLine($"<a href=\"{traceViewerUrl}\" target=\"_blank\">View Trace in External Viewer</a>");
                    html.AppendLine("</div>");
                }
            }
            
            // Add agent hierarchy visualization if rootAgentId is provided
            if (!string.IsNullOrEmpty(rootAgentId))
            {
                var hierarchyDiagram = await GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Agent Hierarchy</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(hierarchyDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
            }
            
            // Add message flow visualization if traceId is provided
            if (!string.IsNullOrEmpty(traceId))
            {
                var messageDiagram = await GenerateMessageFlowMermaidDiagramAsync(traceId);
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Message Flow</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(messageDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
            }
            
            html.AppendLine("</div>");
            
            return html.ToString();
        }

        /// <inheritdoc />
        public string GetTraceViewerUrl(string? traceId = null)
        {
            if (string.IsNullOrEmpty(traceId))
            {
                return string.Empty;
            }
            
            switch (_options.TraceViewerType)
            {
                case TraceViewerType.Jaeger:
                    return $"{_options.JaegerBaseUrl}/trace/{traceId}";
                    
                case TraceViewerType.Zipkin:
                    return $"{_options.ZipkinBaseUrl}/zipkin/traces/{traceId}";
                    
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Builds an agent hierarchy starting from the specified root agent.
        /// </summary>
        /// <param name="rootAgentId">ID of the root agent</param>
        /// <returns>Hierarchical structure representing the agent tree</returns>
        private async Task<AgentHierarchyNode> BuildAgentHierarchyAsync(string rootAgentId)
        {
            if (_agentRegistry == null)
            {
                throw new InvalidOperationException("Agent registry is not available");
            }
            
            // Get the root agent
            var rootAgent = await _agentRegistry.GetAgentByIdAsync(rootAgentId);
            if (rootAgent == null)
            {
                throw new InvalidOperationException($"Agent with ID {rootAgentId} not found");
            }
            
            // Create the root node
            var rootNode = new AgentHierarchyNode
            {
                Id = rootAgentId,
                Name = rootAgent.Name ?? "Unknown",
                Type = rootAgent.GetType().Name,
                Description = rootAgent.Description ?? string.Empty,
                Children = new List<AgentHierarchyNode>()
            };
            
            // Get all agents to build the hierarchy
            var allAgents = await _agentRegistry.GetAllAgentsAsync();
            
            // Find children of the root agent (agents with parentId = rootAgentId)
            var childAgents = allAgents.Where(a => a.ParentAgentId == rootAgentId);
            
            // Recursively build the tree
            foreach (var childAgent in childAgents)
            {
                var childNode = await BuildAgentHierarchyNodeAsync(childAgent.Id, allAgents);
                rootNode.Children.Add(childNode);
            }
            
            return rootNode;
        }

        /// <summary>
        /// Recursively builds an agent hierarchy node for a specific agent.
        /// </summary>
        /// <param name="agentId">ID of the agent</param>
        /// <param name="allAgents">Collection of all agents</param>
        /// <returns>Hierarchical node for the agent</returns>
        private async Task<AgentHierarchyNode> BuildAgentHierarchyNodeAsync(string agentId, IEnumerable<IAgent> allAgents)
        {
            if (_agentRegistry == null)
            {
                throw new InvalidOperationException("Agent registry is not available");
            }
            
            // Get the agent
            var agent = await _agentRegistry.GetAgentByIdAsync(agentId);
            if (agent == null)
            {
                throw new InvalidOperationException($"Agent with ID {agentId} not found");
            }
            
            // Create the node
            var node = new AgentHierarchyNode
            {
                Id = agentId,
                Name = agent.Name ?? "Unknown",
                Type = agent.GetType().Name,
                Description = agent.Description ?? string.Empty,
                Children = new List<AgentHierarchyNode>()
            };
            
            // Find children of this agent
            var childAgents = allAgents.Where(a => a.ParentAgentId == agentId);
            
            // Recursively build the tree
            foreach (var childAgent in childAgents)
            {
                var childNode = await BuildAgentHierarchyNodeAsync(childAgent.Id, allAgents);
                node.Children.Add(childNode);
            }
            
            return node;
        }

        /// <summary>
        /// Generates a Mermaid diagram from an agent hierarchy.
        /// </summary>
        /// <param name="hierarchy">Agent hierarchy node</param>
        /// <returns>Mermaid diagram text</returns>
        private string GenerateMermaidDiagramFromHierarchy(AgentHierarchyNode hierarchy)
        {
            var sb = new StringBuilder();
            
            // Mermaid diagram header
            sb.AppendLine("graph TD");
            
            // Define nodes and connections
            DefineHierarchyNodes(sb, hierarchy);
            
            // Define connections
            DefineHierarchyConnections(sb, hierarchy);
            
            // Add styles
            sb.AppendLine("classDef root fill:#f96,stroke:#333,stroke-width:2px");
            sb.AppendLine("classDef agent fill:#bbf,stroke:#333,stroke-width:1px");
            
            // Apply styles
            sb.AppendLine($"class {SanitizeId(hierarchy.Id)} root");
            
            ApplyAgentStyles(sb, hierarchy);
            
            return sb.ToString();
        }

        /// <summary>
        /// Defines nodes in the Mermaid diagram.
        /// </summary>
        /// <param name="sb">StringBuilder instance</param>
        /// <param name="node">Current node to process</param>
        private void DefineHierarchyNodes(StringBuilder sb, AgentHierarchyNode node)
        {
            // Define the current node
            var sanitizedId = SanitizeId(node.Id);
            var description = string.IsNullOrEmpty(node.Description) ? "" : $"<br/>{TruncateString(node.Description, 30)}";
            sb.AppendLine($"{sanitizedId}[\"{TruncateString(node.Id, 20)}<br/>{node.Type}{description}\"]");
            
            // Define child nodes
            foreach (var child in node.Children)
            {
                DefineHierarchyNodes(sb, child);
            }
        }

        /// <summary>
        /// Defines connections between nodes in the Mermaid diagram.
        /// </summary>
        /// <param name="sb">StringBuilder instance</param>
        /// <param name="node">Current node to process</param>
        private void DefineHierarchyConnections(StringBuilder sb, AgentHierarchyNode node)
        {
            // Define connections to child nodes
            foreach (var child in node.Children)
            {
                sb.AppendLine($"{SanitizeId(node.Id)} --> {SanitizeId(child.Id)}");
                DefineHierarchyConnections(sb, child);
            }
        }

        /// <summary>
        /// Applies styles to agent nodes in the Mermaid diagram.
        /// </summary>
        /// <param name="sb">StringBuilder instance</param>
        /// <param name="node">Current node to process</param>
        private void ApplyAgentStyles(StringBuilder sb, AgentHierarchyNode node)
        {
            // Apply styles to child nodes
            foreach (var child in node.Children)
            {
                sb.AppendLine($"class {SanitizeId(child.Id)} agent");
                ApplyAgentStyles(sb, child);
            }
        }

        /// <summary>
        /// Sanitizes an ID for use in a Mermaid diagram.
        /// </summary>
        /// <param name="id">ID to sanitize</param>
        /// <returns>Sanitized ID</returns>
        private string SanitizeId(string id)
        {
            // Replace characters that might cause issues in Mermaid
            return id.Replace("-", "_").Replace(".", "_").Replace(" ", "_");
        }

        /// <summary>
        /// Truncates a string if it exceeds a maximum length.
        /// </summary>
        /// <param name="str">String to truncate</param>
        /// <param name="maxLength">Maximum length</param>
        /// <returns>Truncated string</returns>
        private string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
            {
                return str;
            }
            
            return str.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Builds a message flow model from a collection of trace activities.
        /// </summary>
        /// <param name="activities">Collection of trace activities</param>
        /// <returns>Message flow diagram model</returns>
        private MessageFlowDiagram BuildMessageFlowFromActivities(IEnumerable<IActivity> activities)
        {
            var messageFlow = new MessageFlowDiagram();
            
            // Create a dictionary to look up activities by ID
            var activityMap = activities.ToDictionary(a => a.Id, a => a);
            
            // Create participants for each unique service/component in the trace
            var participants = new Dictionary<string, MessageFlowParticipant>();
            foreach (var activity in activities)
            {
                var serviceName = activity.DisplayName ?? "Unknown";
                if (!participants.ContainsKey(serviceName))
                {
                    var participant = new MessageFlowParticipant
                    {
                        Id = SanitizeId(serviceName),
                        Name = serviceName
                    };
                    participants[serviceName] = participant;
                    messageFlow.Participants.Add(participant);
                }
            }
            
            // Create messages for each activity and its children
            foreach (var activity in activities)
            {
                if (string.IsNullOrEmpty(activity.ParentId) || !activityMap.ContainsKey(activity.ParentId))
                {
                    // Skip the root activity or activities without a parent
                    continue;
                }
                
                var parent = activityMap[activity.ParentId];
                
                var parentService = parent.DisplayName ?? "Unknown";
                var childService = activity.DisplayName ?? "Unknown";
                
                // Create a message between the parent and child
                var message = new MessageFlowMessage
                {
                    SourceId = SanitizeId(parentService),
                    TargetId = SanitizeId(childService),
                    Message = activity.Name ?? "Request",
                    DurationMs = activity.Duration.TotalMilliseconds,
                    IsAsync = false // Assume synchronous messages for simplicity
                };
                
                messageFlow.Messages.Add(message);
                
                // If the activity has a return value or response, add a response message
                if (activity.HasResult)
                {
                    var responseMessage = new MessageFlowMessage
                    {
                        SourceId = SanitizeId(childService),
                        TargetId = SanitizeId(parentService),
                        Message = "Response",
                        DurationMs = 0, // Response time is included in the request duration
                        IsAsync = false
                    };
                    
                    messageFlow.Messages.Add(responseMessage);
                }
            }
            
            return messageFlow;
        }

        /// <summary>
        /// Generates a Mermaid diagram from a message flow model.
        /// </summary>
        /// <param name="messageFlow">Message flow diagram model</param>
        /// <returns>Mermaid diagram text</returns>
        private string GenerateMermaidDiagramFromMessageFlow(MessageFlowDiagram messageFlow)
        {
            var sb = new StringBuilder();
            
            // Mermaid diagram header
            sb.AppendLine("sequenceDiagram");
            
            // Define participants
            foreach (var participant in messageFlow.Participants)
            {
                sb.AppendLine($"participant {participant.Id} as \"{participant.Name}\"");
            }
            
            // Define messages
            foreach (var message in messageFlow.Messages)
            {
                var arrow = message.IsAsync ? "->>" : "->>";
                var durationText = message.DurationMs > 0 ? $" ({message.DurationMs:F1}ms)" : "";
                sb.AppendLine($"{message.SourceId}{arrow}{message.TargetId}: {message.Message}{durationText}");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Generates a placeholder agent hierarchy diagram when data is not available.
        /// </summary>
        /// <param name="rootAgentId">ID of the root agent</param>
        /// <returns>Mermaid diagram text</returns>
        private string GeneratePlaceholderAgentHierarchy(string rootAgentId)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("graph TD");
            sb.AppendLine($"{SanitizeId(rootAgentId)}[\"{rootAgentId}<br/>Root Agent\"]");
            sb.AppendLine("placeholder1[Child Agent 1]");
            sb.AppendLine("placeholder2[Child Agent 2]");
            sb.AppendLine($"{SanitizeId(rootAgentId)} --> placeholder1");
            sb.AppendLine($"{SanitizeId(rootAgentId)} --> placeholder2");
            sb.AppendLine("classDef root fill:#f96,stroke:#333,stroke-width:2px");
            sb.AppendLine("classDef agent fill:#bbf,stroke:#333,stroke-width:1px");
            sb.AppendLine($"class {SanitizeId(rootAgentId)} root");
            sb.AppendLine("class placeholder1,placeholder2 agent");
            
            return sb.ToString();
        }

        /// <summary>
        /// Generates a placeholder message flow diagram when data is not available.
        /// </summary>
        /// <param name="traceId">ID of the trace</param>
        /// <returns>Mermaid diagram text</returns>
        private string GeneratePlaceholderMessageFlow(string traceId)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("sequenceDiagram");
            sb.AppendLine("participant root as \"Root Agent\"");
            sb.AppendLine("participant child as \"Child Agent\"");
            sb.AppendLine("participant tool as \"External Tool\"");
            sb.AppendLine("root->>child: Process task");
            sb.AppendLine("child->>tool: Call external service");
            sb.AppendLine("tool->>child: Service response");
            sb.AppendLine("child->>root: Task result");
            
            return sb.ToString();
        }
    }
} 