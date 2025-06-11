using System.Threading.Tasks;

namespace AgctorSDK.Core.Utils.Observability.Visualization
{
    /// <summary>
    /// Service for visualizing agent hierarchies, message flows, and other aspects of the Agctor system.
    /// </summary>
    public interface IVisualizationService
    {
        /// <summary>
        /// Generates a Mermaid diagram representing the agent hierarchy.
        /// </summary>
        /// <param name="rootAgentId">ID of the root agent to start the hierarchy from</param>
        /// <returns>Mermaid diagram representation of the agent hierarchy</returns>
        Task<string> GenerateAgentHierarchyMermaidDiagramAsync(string rootAgentId);
        
        /// <summary>
        /// Generates a Mermaid diagram representing the message flow for a specific trace.
        /// </summary>
        /// <param name="traceId">ID of the trace to visualize</param>
        /// <returns>Mermaid diagram representation of the message flow</returns>
        Task<string> GenerateMessageFlowMermaidDiagramAsync(string traceId);
        
        /// <summary>
        /// Generates an HTML representation of visualizations for the specified agent and/or trace.
        /// </summary>
        /// <param name="rootAgentId">Optional ID of the root agent to visualize hierarchy</param>
        /// <param name="traceId">Optional ID of the trace to visualize message flow</param>
        /// <returns>HTML representation of the visualizations</returns>
        Task<string> GenerateVisualizationHtmlAsync(string? rootAgentId = null, string? traceId = null);
        
        /// <summary>
        /// Gets the URL for viewing a trace in an external trace viewer (e.g., Jaeger, Zipkin).
        /// </summary>
        /// <param name="traceId">Optional ID of the trace to view</param>
        /// <returns>URL to the trace viewer</returns>
        string GetTraceViewerUrl(string? traceId = null);
    }
} 