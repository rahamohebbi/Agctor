using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.Utils.Observability.Visualization
{
    /// <summary>
    /// Extension methods for visualization services.
    /// </summary>
    public static class VisualizationExtensions
    {
        /// <summary>
        /// Adds the visualization service to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureOptions">Optional action to configure visualization options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAgctorVisualization(
            this IServiceCollection services,
            Action<VisualizationOptions>? configureOptions = null)
        {
            // Register default options
            var options = new VisualizationOptions();
            configureOptions?.Invoke(options);
            services.AddSingleton(options);
            
            // Register the visualization service
            services.AddSingleton<IVisualizationService, VisualizationService>();

            return services;
        }
        
        /// <summary>
        /// Generates an HTML fragment with links to visualization resources.
        /// </summary>
        /// <param name="visualizationService">The visualization service.</param>
        /// <param name="rootAgentId">Optional root agent ID for agent hierarchy visualization.</param>
        /// <param name="traceId">Optional trace ID for message flow visualization.</param>
        /// <returns>HTML fragment with visualization links.</returns>
        public static async Task<string> GenerateVisualizationHtmlAsync(
            this IVisualizationService visualizationService,
            string? rootAgentId = null,
            string? traceId = null)
        {
            var links = new List<string>();
            
            // Add trace viewer link if we have a trace ID
            if (traceId != null)
            {
                string traceViewerUrl = visualizationService.GetTraceViewerUrl(traceId);
                if (!string.IsNullOrEmpty(traceViewerUrl))
                {
                    links.Add($"<a href=\"{traceViewerUrl}\" target=\"_blank\">View Trace in External Viewer</a>");
                }
            }
            else
            {
                string traceViewerUrl = visualizationService.GetTraceViewerUrl();
                if (!string.IsNullOrEmpty(traceViewerUrl))
                {
                    links.Add($"<a href=\"{traceViewerUrl}\" target=\"_blank\">Open Trace Explorer</a>");
                }
            }
            
            // Generate HTML
            string html = "<div class=\"agctor-visualization\">";
            
            // Add links section if we have any
            if (links.Count > 0)
            {
                html += "<div class=\"viz-links\">";
                html += string.Join(" | ", links);
                html += "</div>";
            }
            
            // Add agent hierarchy if we have a root agent ID
            if (rootAgentId != null)
            {
                try
                {
                    string mermaidDiagram = await visualizationService.GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);
                    html += "<div class=\"viz-section\">";
                    html += "<h3>Agent Hierarchy</h3>";
                    html += "<div class=\"mermaid\">";
                    html += mermaidDiagram;
                    html += "</div>";
                    html += "</div>";
                }
                catch (Exception ex)
                {
                    html += $"<div class=\"viz-error\">Error generating agent hierarchy: {ex.Message}</div>";
                }
            }
            
            // Add message flow if we have a trace ID
            if (traceId != null)
            {
                try
                {
                    string mermaidDiagram = await visualizationService.GenerateMessageFlowMermaidDiagramAsync(traceId);
                    html += "<div class=\"viz-section\">";
                    html += "<h3>Message Flow</h3>";
                    html += "<div class=\"mermaid\">";
                    html += mermaidDiagram;
                    html += "</div>";
                    html += "</div>";
                }
                catch (Exception ex)
                {
                    html += $"<div class=\"viz-error\">Error generating message flow: {ex.Message}</div>";
                }
            }
            
            html += "</div>";
            
            // Add Mermaid JS initialization script
            html += "<script type=\"text/javascript\">";
            html += "// Initialize Mermaid diagrams if Mermaid is available";
            html += "if (typeof mermaid !== 'undefined') {";
            html += "    mermaid.initialize({ startOnLoad: true });";
            html += "} else {";
            html += "    console.warn('Mermaid JS not loaded. Diagrams will not render properly.');";
            html += "}";
            html += "</script>";
            
            return html;
        }
        
        /// <summary>
        /// Gets the HTML needed to include Mermaid JS in a web page.
        /// </summary>
        /// <returns>HTML script tag for including Mermaid JS.</returns>
        public static string GetMermaidJsInclude()
        {
            return "<script src=\"https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js\"></script>";
        }
        
        /// <summary>
        /// Gets CSS styles for visualization components.
        /// </summary>
        /// <returns>CSS styles as a string.</returns>
        public static string GetVisualizationCss()
        {
            return @"
<style>
    .agctor-visualization {
        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
        margin: 20px 0;
        padding: 15px;
        border: 1px solid #e0e0e0;
        border-radius: 5px;
        background-color: #f9f9f9;
    }
    
    .viz-links {
        margin-bottom: 15px;
        padding-bottom: 10px;
        border-bottom: 1px solid #e0e0e0;
    }
    
    .viz-links a {
        color: #0066cc;
        text-decoration: none;
        padding: 5px 10px;
        border-radius: 3px;
        background-color: #f0f0f0;
    }
    
    .viz-links a:hover {
        background-color: #e0e0e0;
    }
    
    .viz-section {
        margin-top: 15px;
    }
    
    .viz-section h3 {
        margin: 0 0 10px 0;
        font-size: 16px;
        font-weight: 600;
    }
    
    .viz-error {
        color: #cc0000;
        padding: 10px;
        background-color: #ffeeee;
        border-radius: 3px;
    }
    
    .mermaid {
        font-size: 14px;
        background-color: white;
        padding: 15px;
        border-radius: 5px;
        border: 1px solid #e0e0e0;
        overflow: auto;
    }
</style>";
        }
    }
} 